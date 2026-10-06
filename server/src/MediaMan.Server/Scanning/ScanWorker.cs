using MediaMan.Library.Importing;
using MediaMan.Library.Matching;
using MediaMan.Library.Scanning;
using MediaMan.Server.LibraryApi;

namespace MediaMan.Server.Scanning;

/// <summary>
/// Runs queued library scans one at a time: scan, then match, then import (SPEC §7.2). Each phase gets its own DI
/// scope, so a phase starts from a clean change tracker and only ids pass between them.
/// </summary>
public sealed partial class ScanWorker(ScanCoordinator coordinator, IServiceScopeFactory scopes, ILogger<ScanWorker> logger)
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
            // Shutting down. Work a scan hadn't saved is redone next time: unprocessed files are still Pending.
        }
    }

    private async Task RunAsync(Guid libraryId, CancellationToken ct)
    {
        coordinator.Start(libraryId);
        ScanResult? scan = null;
        MatchRunResult? match = null;
        ImportResult? import = null;
        try
        {
            scan = await InScopeAsync((LibraryScanner s) => s.ScanAsync(libraryId, ct));
            coordinator.Enter(libraryId, ScanPhase.Matching);
            match = await InScopeAsync((LibraryMatcher m) => m.MatchPendingAsync(libraryId, ct));
            coordinator.Enter(libraryId, ScanPhase.Importing);
            import = await InScopeAsync((MetadataImporter i) => i.ImportAsync(match.Decisions, ct));
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
            coordinator.Finish(libraryId, succeeded: false, Counts(scan, match, import),
                new ScanError(FailedCode, "The scan stopped with an unexpected error. The server log has the details; scan again to retry."));
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

    [LoggerMessage(Level = LogLevel.Error, Message = "The scan of library {LibraryId} failed")]
    private static partial void LogFailed(ILogger logger, Exception exception, Guid libraryId);
}
