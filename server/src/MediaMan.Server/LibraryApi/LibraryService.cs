using MediaMan.Core;
using MediaMan.Core.Domain;
using MediaMan.Data;
using MediaMan.Server.Hosting;
using MediaMan.Server.Scanning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MediaMan.Server.LibraryApi;

public sealed record LibraryCreateRequest(string? Name, string? ContentType, IReadOnlyList<string?>? RootPaths, int? ScanIntervalMinutes);

public sealed record LibraryUpdateRequest(string? Name, string? ContentType, IReadOnlyList<string?>? RootPaths, int? ScanIntervalMinutes);

public enum LibraryFailure
{
    /// <summary>Some fields are invalid; <see cref="LibraryOutcome.Errors"/> says which.</summary>
    Invalid,
    NotFound,

    /// <summary>The library's scan is running, so its roots can't change.</summary>
    ScanRunning,
}

public sealed record LibraryOutcome(LibraryDto? Library, LibraryFailure? Failure = null, IDictionary<string, string[]>? Errors = null)
{
    public static LibraryOutcome Failed(LibraryFailure failure) => new(null, failure);
}

/// <summary>
/// Adds, changes and removes External libraries (SPEC §8 libraries). Roots are only read, never written; removing a
/// root or a library deletes database rows only (SPEC §5).
/// </summary>
public sealed class LibraryService(
    MediaManDbContext db,
    LibraryQueries queries,
    ScanCoordinator scans,
    IOptions<MediaManOptions> options,
    IClock clock)
{
    /// <summary>Default full-scan interval, 6 hours (PLAN 2.8).</summary>
    public const int DefaultScanIntervalMinutes = 360;

    public const int MaximumNameLength = 100;
    public const int MaximumRoots = 20;
    public const int MinimumScanIntervalMinutes = 15;
    public const int MaximumScanIntervalMinutes = 7 * 24 * 60;

    // Overlap checks read every library's roots, so changes to roots run one at a time.
    private static readonly SemaphoreSlim _writes = new(1, 1);

    public async Task<LibraryOutcome> CreateAsync(LibraryCreateRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (!Wire.TryParse<ContentType>(request.ContentType, out var contentType))
        {
            errors["contentType"] = [ContentTypeMessage];
        }

        var name = ReadName(request.Name, errors);
        var interval = ReadInterval(request.ScanIntervalMinutes, errors);

        await _writes.WaitAsync(ct);
        try
        {
            var roots = await ReadRootsAsync(request.RootPaths, libraryId: null, errors, ct);
            if (errors.Count > 0)
            {
                return new LibraryOutcome(null, LibraryFailure.Invalid, errors);
            }

            var library = new Core.Domain.Library
            {
                Id = Ids.New(clock),
                Name = name ?? LibraryRoots.NameOf(roots[0]),
                Kind = LibraryKind.External,
                ContentType = contentType,
                RootPaths = roots,
                ScanIntervalMinutes = interval ?? DefaultScanIntervalMinutes,
            };
            db.Libraries.Add(library);
            await db.SaveChangesAsync(ct);
            QueueScan(library.Id);
            return new LibraryOutcome(await queries.GetLibraryAsync(library.Id, ct));
        }
        finally
        {
            _writes.Release();
        }
    }

    public async Task<LibraryOutcome> UpdateAsync(Guid libraryId, LibraryUpdateRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _writes.WaitAsync(ct);
        try
        {
            var library = await db.Libraries.FirstOrDefaultAsync(l => l.Id == libraryId, ct);
            if (library is null)
            {
                return LibraryOutcome.Failed(LibraryFailure.NotFound);
            }

            var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
            var contentType = library.ContentType;
            if (request.ContentType is not null && !Wire.TryParse(request.ContentType, out contentType))
            {
                errors["contentType"] = [ContentTypeMessage];
            }

            var name = request.Name is null ? null : ReadName(request.Name, errors) ?? Invalid(errors, "name", "Enter a name for the library.");
            var interval = ReadInterval(request.ScanIntervalMinutes, errors);
            var roots = request.RootPaths is null ? null : await ReadRootsAsync(request.RootPaths, libraryId, errors, ct);
            if (errors.Count > 0)
            {
                return new LibraryOutcome(null, LibraryFailure.Invalid, errors);
            }

            var removed = roots is null ? [] : library.RootPaths.Where(r => !roots.Any(k => k.Equals(r, LibraryRoots.Comparison))).ToList();
            var added = roots is not null && roots.Any(r => !library.RootPaths.Any(k => k.Equals(r, LibraryRoots.Comparison)));
            if ((removed.Count > 0 || added) && scans.Get(libraryId).State == Wire.Name(ScanStatus.Running))
            {
                return LibraryOutcome.Failed(LibraryFailure.ScanRunning);
            }

            library.Name = name ?? library.Name;
            library.ContentType = contentType;
            library.ScanIntervalMinutes = interval ?? library.ScanIntervalMinutes;
            if (roots is not null)
            {
                library.RootPaths = roots;
            }

            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await db.SaveChangesAsync(ct);
            await RemoveFilesUnderAsync(libraryId, removed, ct);
            await transaction.CommitAsync(ct);

            if (added)
            {
                QueueScan(libraryId);
            }

            return new LibraryOutcome(await queries.GetLibraryAsync(libraryId, ct));
        }
        finally
        {
            _writes.Release();
        }
    }

    /// <summary>Removes the library with its files, links and reviews. Titles stay, hidden until a file matches them again.</summary>
    public async Task<LibraryFailure?> DeleteAsync(Guid libraryId, CancellationToken ct)
    {
        await _writes.WaitAsync(ct);
        try
        {
            if (!await db.Libraries.AnyAsync(l => l.Id == libraryId, ct))
            {
                return LibraryFailure.NotFound;
            }

            if (!scans.TryForget(libraryId))
            {
                return LibraryFailure.ScanRunning;
            }

            // MediaFiles, and through them FileLinks and MatchReviews, go by the database's cascades.
            await db.Libraries.Where(l => l.Id == libraryId).ExecuteDeleteAsync(ct);
            return null;
        }
        finally
        {
            _writes.Release();
        }
    }

    private const string ContentTypeMessage = "Choose what the library contains: movies, shows, anime or mixed.";

    private void QueueScan(Guid libraryId)
    {
        if (scans.IsAvailable)
        {
            scans.Request(libraryId);
        }
    }

    /// <summary>Deletes the rows of files under removed roots; the files themselves are untouched (SPEC §5).</summary>
    private async Task RemoveFilesUnderAsync(Guid libraryId, List<string> removedRoots, CancellationToken ct)
    {
        if (removedRoots.Count == 0)
        {
            return;
        }

        var files = await db.MediaFiles.AsNoTracking()
            .Where(f => f.LibraryId == libraryId)
            .Select(f => new { f.Id, f.Path })
            .ToListAsync(ct);
        var gone = files.Where(f => removedRoots.Any(r => LibraryRoots.IsUnder(f.Path, r))).Select(f => f.Id).ToList();
        foreach (var chunk in gone.Chunk(500))
        {
            await db.MediaFiles.Where(f => chunk.Contains(f.Id)).ExecuteDeleteAsync(ct);
        }
    }

    private static string? ReadName(string? name, Dictionary<string, string[]> errors)
    {
        name = name?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        if (name.Length > MaximumNameLength)
        {
            errors["name"] = [$"Use a name of at most {MaximumNameLength} characters."];
            return null;
        }

        return name;
    }

    private static int? ReadInterval(int? minutes, Dictionary<string, string[]> errors)
    {
        if (minutes is null or 0 or (>= MinimumScanIntervalMinutes and <= MaximumScanIntervalMinutes))
        {
            return minutes;
        }

        errors["scanIntervalMinutes"] =
            [$"Scan every {MinimumScanIntervalMinutes} to {MaximumScanIntervalMinutes} minutes (a week), or enter 0 for no scheduled scans."];
        return null;
    }

    /// <summary>The normalized roots, with an error per bad root keyed <c>rootPaths[N]</c>, or for the list as <c>rootPaths</c>.</summary>
    private async Task<List<string>> ReadRootsAsync(
        IReadOnlyList<string?>? paths, Guid? libraryId, Dictionary<string, string[]> errors, CancellationToken ct)
    {
        if (paths is null || paths.Count == 0)
        {
            errors["rootPaths"] = ["Add at least one folder that holds your media."];
            return [];
        }

        if (paths.Count > MaximumRoots)
        {
            errors["rootPaths"] = [$"A library can have at most {MaximumRoots} folders. Split them over more libraries."];
            return [];
        }

        var others = await db.Libraries.AsNoTracking()
            .Where(l => l.Id != libraryId)
            .Select(l => new { l.Name, l.RootPaths })
            .ToListAsync(ct);
        var dataDirectory = options.Value.DataDirectory;
        var roots = new List<string>(paths.Count);
        for (var i = 0; i < paths.Count; i++)
        {
            var key = $"rootPaths[{i}]";
            if (LibraryRoots.PathProblem(paths[i], out var root) is { } problem)
            {
                errors[key] = [problem];
                continue;
            }

            if (LibraryRoots.DataDirectoryProblem(root, dataDirectory) is { } dataProblem)
            {
                errors[key] = [dataProblem];
            }
            else if (roots.FirstOrDefault(r => LibraryRoots.Overlap(r, root)) is { } sibling)
            {
                errors[key] = [sibling.Equals(root, LibraryRoots.Comparison)
                    ? "This folder is listed twice. Remove one of them."
                    : $"This folder overlaps '{sibling}' in this library. Keep only the outer folder."];
            }
            else if (others.FirstOrDefault(o => o.RootPaths.Any(r => LibraryRoots.Overlap(r, root))) is { } other)
            {
                errors[key] = [$"This folder overlaps the library '{other.Name}'. Each folder can belong to one library only; change that library first."];
            }

            roots.Add(root);
        }

        return roots;
    }

    private static string? Invalid(Dictionary<string, string[]> errors, string key, string message)
    {
        errors.TryAdd(key, [message]);
        return null;
    }
}
