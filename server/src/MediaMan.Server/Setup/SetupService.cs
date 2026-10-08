using MediaMan.Auth;
using MediaMan.Core;
using MediaMan.Core.Domain;
using MediaMan.Data;
using MediaMan.Server.Hosting;
using MediaMan.Server.LibraryApi;
using MediaMan.Server.Scanning;

namespace MediaMan.Server.Setup;

public sealed record SetupRequest(string? Username, string? Password, string? Mode, SetupLibraryRequest? Library, string? DeviceName);

public sealed record SetupLibraryRequest(string? Path, string? ContentType, string? Name);

public enum SetupFailure
{
    /// <summary>Some fields are invalid; <see cref="SetupOutcome.Errors"/> says which.</summary>
    Invalid,

    /// <summary>A Superadmin already exists.</summary>
    AlreadyComplete,
}

public sealed record SetupOutcome(AuthTokens? Tokens, SetupFailure? Failure, IDictionary<string, string[]>? Errors);

/// <summary>
/// First-run setup (SPEC §7.1): creates the Superadmin, records the mode and adds the first library as one transaction,
/// then signs the Superadmin in and queues the first scan. The library folder is only read, never written (SPEC §5).
/// </summary>
public sealed class SetupService(
    MediaManDbContext db,
    AccountService accounts,
    SessionService sessions,
    SetupState state,
    ScanCoordinator scans,
    IClock clock)
{
    /// <summary>Default full-scan interval, 6 hours (PLAN 2.8).</summary>
    public const int DefaultScanIntervalMinutes = 360;

    private static readonly SetupOutcome _alreadyComplete = new(null, SetupFailure.AlreadyComplete, null);

    public async Task<SetupOutcome> CompleteAsync(SetupRequest request, string? fallbackDeviceName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (await state.IsCompleteAsync(db, cancellationToken))
        {
            return _alreadyComplete;
        }

        var errors = Validate(request, out var mode, out var contentType, out var rootPath);
        if (errors.Count > 0)
        {
            return new SetupOutcome(null, SetupFailure.Invalid, errors);
        }

        await state.Lock.WaitAsync(cancellationToken);
        try
        {
            if (await state.IsCompleteAsync(db, cancellationToken))
            {
                return _alreadyComplete;
            }

            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            User user;
            try
            {
                user = await accounts.CreateSuperadminAsync(request.Username!, request.Password!, cancellationToken);
            }
            catch (InvalidOperationException)
            {
                return _alreadyComplete; // another process set up first; the transaction rolls back on dispose
            }

            await ServerModes.RecordAsync(db, mode, cancellationToken);
            var name = request.Library!.Name?.Trim();
            var library = new Core.Domain.Library
            {
                Id = Ids.New(clock),
                Name = string.IsNullOrEmpty(name) ? NameOf(rootPath) : name,
                Kind = LibraryKind.External,
                ContentType = contentType,
                RootPaths = [rootPath],
                ScanIntervalMinutes = DefaultScanIntervalMinutes,
            };
            db.Libraries.Add(library);
            await db.SaveChangesAsync(cancellationToken);

            var tokens = await sessions.StartAsync(user.Id, request.DeviceName ?? fallbackDeviceName, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            state.MarkComplete();

            if (scans.IsAvailable)
            {
                scans.Request(library.Id);
            }

            return new SetupOutcome(tokens, null, null);
        }
        finally
        {
            state.Lock.Release();
        }
    }

    /// <summary>Every invalid field at once, keyed as the contract names them, so the wizard can show each inline.</summary>
    private static Dictionary<string, string[]> Validate(
        SetupRequest request, out ServerMode mode, out ContentType contentType, out string rootPath)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        contentType = default;
        rootPath = "";

        var username = request.Username?.Trim() ?? "";
        if (username.Length is 0 or > AccountService.MaximumUsernameLength)
        {
            errors["username"] = [$"Enter a username of 1 to {AccountService.MaximumUsernameLength} characters."];
        }

        var password = request.Password ?? "";
        if (password.Length < AccountService.MinimumPasswordLength)
        {
            errors["password"] = [$"Use a password of at least {AccountService.MinimumPasswordLength} characters."];
        }
        else if (password.Length > PasswordHasher.MaximumPasswordLength)
        {
            errors["password"] = [$"Use a password of at most {PasswordHasher.MaximumPasswordLength} characters."];
        }

        if (!Wire.TryParse(request.Mode, out mode))
        {
            errors["mode"] = ["Choose 'local' (this computer only) or 'server' (the local network)."];
        }

        if (request.Library is not { } library)
        {
            errors["library"] = ["Add a first library: a folder and what it contains."];
            return errors;
        }

        if (!Wire.TryParse(library.ContentType, out contentType))
        {
            errors["library.contentType"] = ["Choose what the folder contains: movies, shows, anime or mixed."];
        }

        if (PathProblem(library.Path, out rootPath) is { } problem)
        {
            errors["library.path"] = [problem];
        }

        return errors;
    }

    /// <summary>Why the folder can't be a library root, or null with its normalized full path.</summary>
    private static string? PathProblem(string? path, out string rootPath)
    {
        rootPath = "";
        path = path?.Trim();
        if (string.IsNullOrEmpty(path))
        {
            return "Enter the folder that holds your media.";
        }

        if (!Path.IsPathFullyQualified(path))
        {
            return "Enter the full path to the folder, starting from the drive or root.";
        }

        rootPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (!Directory.Exists(rootPath))
        {
            return "This folder doesn't exist or can't be reached. Check the path, and that the drive is connected.";
        }

        try
        {
            using var entries = Directory.EnumerateFileSystemEntries(rootPath).GetEnumerator();
            entries.MoveNext();
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException)
        {
            return "MediaMan can't read this folder. Give the account the server runs as permission to read it, or pick another folder.";
        }

        return null;
    }

    /// <summary>The folder's name; a drive or filesystem root has none, so it's named by its path.</summary>
    private static string NameOf(string rootPath) =>
        Path.GetFileName(rootPath) is { Length: > 0 } name ? name : rootPath;
}
