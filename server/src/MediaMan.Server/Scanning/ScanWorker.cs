using MediaMan.Core;
using MediaMan.Data;
using MediaMan.Library.Images;
using MediaMan.Library.Importing;
using MediaMan.Library.Matching;
using MediaMan.Library.Scanning;
using MediaMan.Server.LibraryApi;
using Microsoft.EntityFrameworkCore;

namespace MediaMan.Server.Scanning;

/// <summary>
/// Runs queued library scans one at a time: scan, match, import, then cache artwork (SPEC §7.2). Each phase gets its
/// own DI scope, so a phase starts from a clean change tracker and only ids pass between them. The library records when
/// a scan starts and finishes, so <see cref="ScanScheduler"/> can resume an interrupted one and time the next.
/// </summary>
public sealed partial class ScanWorker(ScanCoordinator coordinator, IServiceScopeFactory scopes, IClock clock, ILogger<ScanWorker> logger)
    : BackgroundService
{
    public const string FailedCode = "scan.failed";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var libraryId in coordinator.Queue.ReadAllAsync(stoppingToken))
            {
                await RunAsync(libraryId, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down. The library keeps its start time, so the scan resumes at the next startup. Work a scan hadn't saved is redone next time: unprocessed files are still Pending.
        }
    }

    private async Task RunAsync(Guid libraryId, CancellationToken ct)
    {
        if (!coordinator.TryStart(libraryId))
        {
            return;
        }

        ScanResult? scan = null;
        MatchRunResult? match = null;
        ImportResult? import = null;
        try
        {
            await RecordStartAsync(libraryId, ct);
            scan = await InScopeAsync((LibraryScanner s) => s.ScanAsync(libraryId, ct));
            coordinator.Enter(libraryId, ScanPhase.Matching);
            match = await InScopeAsync((LibraryMatcher m) => m.MatchPendingAsync(libraryId, ct));
            coordinator.Enter(libraryId, ScanPhase.Importing);
            import = await InScopeAsync((MetadataImporter i) => i.ImportAsync(match.Decisions, ct));
            coordinator.Enter(libraryId, ScanPhase.Artwork);
            var artwork = await InScopeAsync((ArtworkCache a) => a.CacheMissingAsync(ct));
            if (artwork.Failed > 0)
            {
                LogArtworkFailed(logger, artwork.Failed, libraryId);
            }

            await RecordFinishAsync(libraryId, ct);
            coordinator.Finish(libraryId, succeeded: true, Counts(scan, match, import), error: null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            coordinator.Finish(libraryId, succeeded: false, Counts(scan, match, import),
                new ScanError("scan.cancelled", "The server stopped before the scan finished. Scan again to pick up where it left off."));
            throw;
        }
#pragma warning disable CA1031 // One library's failure must not stop the worker; it's logged and reported on the library.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogFailed(logger, ex, libraryId);
            await RecordFinishAsync(libraryId, ct);
            coordinator.Finish(libraryId, succeeded: false, Counts(scan, match, import),
                new ScanError(FailedCode, "The scan stopped with an unexpected error. The server log has the details; scan again to retry."));
        }
    }

    private Task<int> RecordStartAsync(Guid libraryId, CancellationToken ct)
    {
        var now = clock.UtcNow;
        return InScopeAsync((MediaManDbContext db) => db.Libraries.Where(l => l.Id == libraryId)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.ScanStartedAt, now), ct));
    }

    // A failed scan counts as finished too: retrying it at every startup would only fail again, and the next periodic
    // or requested scan retries it anyway. Failing to record it is logged, not thrown, so the scan still reports.
    private async Task RecordFinishAsync(Guid libraryId, CancellationToken ct)
    {
        var now = clock.UtcNow;
        try
        {
            await InScopeAsync((MediaManDbContext db) => db.Libraries.Where(l => l.Id == libraryId)
                .ExecuteUpdateAsync(s => s.SetProperty(l => l.ScanStartedAt, (DateTimeOffset?)null).SetProperty(l => l.LastScanFinishedAt, now), ct));
        }
#pragma warning disable CA1031 // Logged; the scan outcome matters more than its timestamp.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            LogRecordFailed(logger, ex, libraryId);
        }
    }

    private async Task<TResult> InScopeAsync<TService, TResult>(Func<TService, Task<TResult>> run)
        where TService : notnull
    {
        await using var scope = scopes.CreateAsyncScope();
        return await run(scope.ServiceProvider.GetRequiredService<TService>());
    }

    private static ScanCounts Counts(ScanResult? scan, MatchRunResult? match, ImportResult? import) => new(
        scan?.Added ?? 0,
        scan?.Changed ?? 0,
        scan?.Unchanged ?? 0,
        scan?.Restored ?? 0,
        scan?.Missing ?? 0,
        scan?.Unreadable ?? 0,
        scan?.Purged ?? 0,
        import?.Linked ?? 0,
        (match?.Reviewed ?? 0) + (import?.NeedsAttention ?? 0),
        (match?.Deferred ?? 0) + (import?.Deferred ?? 0));

    // Not a scan failure: the images are fetched again on the next scan or when a client asks for them.
    [LoggerMessage(Level = LogLevel.Warning, Message = "{Count} artwork images couldn't be fetched while scanning library {LibraryId}")]
    private static partial void LogArtworkFailed(ILogger logger, int count, Guid libraryId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't record when the scan of library {LibraryId} finished")]
    private static partial void LogRecordFailed(ILogger logger, Exception exception, Guid libraryId);

    [LoggerMessage(Level = LogLevel.Error, Message = "The scan of library {LibraryId} failed")]
    private static partial void LogFailed(ILogger logger, Exception exception, Guid libraryId);
}
