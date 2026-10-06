using MediaMan.Core;
using MediaMan.Core.Domain;
using MediaMan.Data;
using MediaMan.Server.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MediaMan.Server.Dev;

/// <summary>
/// Registers the dev media folder as an External library on startup, once. It only reads the folder;
/// nothing is ever written under it (SPEC §5).
/// </summary>
public sealed partial class DevLibrarySeeder(
    IServiceScopeFactory scopes,
    IOptions<MediaManOptions> options,
    IClock clock,
    ILogger<DevLibrarySeeder> logger) : IHostedService
{
    /// <summary>Default full-scan interval, 6 hours (PLAN 2.8).</summary>
    public const int DefaultScanIntervalMinutes = 360;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var dev = options.Value.Dev;
        var mediaPath = Normalize(dev.MediaPath!);
        if (!Directory.Exists(mediaPath))
        {
            throw new InvalidOperationException(
                $"The dev media folder '{mediaPath}' doesn't exist. " +
                $"Set {MediaManOptions.SectionName}:Dev:MediaPath to an existing folder.");
        }

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MediaManDbContext>();
        var libraries = await db.Libraries.AsNoTracking().ToListAsync(cancellationToken);
        if (libraries.Any(l => l.RootPaths.Any(r => PathsEqual(Normalize(r), mediaPath))))
        {
            return;
        }

        db.Libraries.Add(new Library
        {
            Id = Ids.New(clock),
            Name = string.IsNullOrWhiteSpace(dev.LibraryName) ? Path.GetFileName(mediaPath) : dev.LibraryName,
            Kind = LibraryKind.External,
            ContentType = dev.ContentType,
            RootPaths = [mediaPath],
            ScanIntervalMinutes = DefaultScanIntervalMinutes,
        });
        await db.SaveChangesAsync(cancellationToken);
        LogRegistered(logger, mediaPath);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static bool PathsEqual(string a, string b) =>
        string.Equals(a, b, OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);

    [LoggerMessage(Level = LogLevel.Information, Message = "Registered dev media folder {MediaPath} as an External library")]
    private static partial void LogRegistered(ILogger logger, string mediaPath);
}
