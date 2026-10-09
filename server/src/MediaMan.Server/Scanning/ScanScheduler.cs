using MediaMan.Core;
using MediaMan.Core.Modules;
using MediaMan.Data;
using MediaMan.Server.LibraryApi;
using Microsoft.EntityFrameworkCore;

namespace MediaMan.Server.Scanning;

/// <summary>
/// Decides when libraries scan without being asked (SPEC §7.2 step 8): at startup it requeues scans the server stopped
/// in the middle of, file watchers queue a scan once a library's changes have settled, and each library gets a full
/// scan every <see cref="Core.Domain.Library.ScanIntervalMinutes"/> for shares where watching is unreliable. Every
/// time comparison reads <see cref="IClock"/>; the real delay between ticks only sets how often it looks.
/// </summary>
public sealed partial class ScanScheduler(
    ScanCoordinator coordinator,
    IServiceScopeFactory scopes,
    IFileWatcher watcher,
    IClock clock,
    ILogger<ScanScheduler> logger) : BackgroundService
{
    public static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(15);

    /// <summary>How long a library's files must be quiet before its changes are scanned, so a copy in progress scans once.</summary>
    public static readonly TimeSpan Debounce = TimeSpan.FromSeconds(10);

    private readonly Lock _lock = new();
    private readonly Dictionary<Guid, DateTimeOffset> _lastChange = [];
    private readonly Dictionary<Guid, Watched> _watched = [];
    private readonly HashSet<string> _reportedFailures = new(StringComparer.Ordinal);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!coordinator.IsAvailable)
        {
            return;
        }

        try
        {
            await ResumeInterruptedAsync(stoppingToken);
            while (true)
            {
                try
                {
                    await TickAsync(stoppingToken);
                }
#pragma warning disable CA1031 // A failed tick (e.g. the database busy) is retried on the next one; it must not stop scheduling.
                catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
                {
                    LogTickFailed(logger, ex);
                }

                await Task.Delay(TickInterval, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
    }

    public override void Dispose()
    {
        foreach (var watched in _watched.Values)
        {
            watched.Dispose();
        }

        _watched.Clear();
        base.Dispose();
    }

    /// <summary>Queues every library whose last scan started and never finished. The scanner saves as it goes, so it picks up where it stopped.</summary>
    internal async Task ResumeInterruptedAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var interrupted = await scope.ServiceProvider.GetRequiredService<MediaManDbContext>().Libraries
            .Where(l => l.ScanStartedAt != null)
            .Select(l => l.Id)
            .ToListAsync(ct);
        foreach (var libraryId in interrupted)
        {
            LogResuming(logger, libraryId);
            coordinator.Request(libraryId);
        }
    }

    /// <summary>Brings the watchers in line with the libraries, then queues the scans that are due.</summary>
    internal async Task TickAsync(CancellationToken ct)
    {
        List<Scheduled> libraries;
        await using (var scope = scopes.CreateAsyncScope())
        {
            libraries = await scope.ServiceProvider.GetRequiredService<MediaManDbContext>().Libraries.AsNoTracking()
                .Select(l => new Scheduled(l.Id, l.RootPaths, l.ScanIntervalMinutes, l.LastScanFinishedAt))
                .ToListAsync(ct);
        }

        UpdateWatchers(libraries);
        var now = clock.UtcNow;
        foreach (var library in libraries)
        {
            if (ChangesSettled(library.Id, now))
            {
                // A running scan may already have walked past the change, so the changes wait for the next tick.
                if (coordinator.Request(library.Id).State != Wire.Name(ScanStatus.Running))
                {
                    ClearChanges(library.Id, now);
                }
            }
            else if (library.IntervalMinutes > 0
                && (library.LastFinishedAt is not { } last || now - last >= TimeSpan.FromMinutes(library.IntervalMinutes)))
            {
                coordinator.Request(library.Id);
            }
        }
    }

    private void UpdateWatchers(List<Scheduled> libraries)
    {
        var current = libraries.ToDictionary(l => l.Id);
        foreach (var gone in _watched.Keys.Where(id => !current.TryGetValue(id, out var l) || !_watched[id].Watches(l.Roots)).ToList())
        {
            _watched[gone].Dispose();
            _watched.Remove(gone);
            if (!current.ContainsKey(gone))
            {
                lock (_lock)
                {
                    _lastChange.Remove(gone);
                }
            }
        }

        foreach (var library in libraries)
        {
            if (!_watched.TryGetValue(library.Id, out var watched))
            {
                watched = new Watched(library.Roots);
                _watched[library.Id] = watched;
            }

            // Roots that couldn't be watched (not mounted yet, say) are tried again every tick.
            foreach (var root in watched.Unwatched())
            {
                try
                {
                    var libraryId = library.Id;
                    watched.Handles[root] = watcher.Watch(root, _ => RecordChange(libraryId));
                    _reportedFailures.Remove(root);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    if (_reportedFailures.Add(root))
                    {
                        LogWatchFailed(logger, ex, root, library.Id);
                    }
                }
            }
        }
    }

    private void RecordChange(Guid libraryId)
    {
        lock (_lock)
        {
            _lastChange[libraryId] = clock.UtcNow;
        }
    }

    private bool ChangesSettled(Guid libraryId, DateTimeOffset now)
    {
        lock (_lock)
        {
            return _lastChange.TryGetValue(libraryId, out var last) && now - last >= Debounce;
        }
    }

    // Only changes seen up to now are cleared; one arriving during this tick waits for its own debounce.
    private void ClearChanges(Guid libraryId, DateTimeOffset now)
    {
        lock (_lock)
        {
            if (_lastChange.TryGetValue(libraryId, out var last) && last <= now)
            {
                _lastChange.Remove(libraryId);
            }
        }
    }

    private sealed record Scheduled(Guid Id, List<string> Roots, int IntervalMinutes, DateTimeOffset? LastFinishedAt);

    /// <summary>One library's watches by root; a null handle is a root that couldn't be watched yet.</summary>
    private sealed class Watched(List<string> roots) : IDisposable
    {
        public Dictionary<string, IDisposable?> Handles { get; } = roots.Distinct(StringComparer.Ordinal).ToDictionary(r => r, IDisposable? (_) => null, StringComparer.Ordinal);

        public bool Watches(List<string> other) => Handles.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(other);

        public List<string> Unwatched() => [.. Handles.Where(h => h.Value is null).Select(h => h.Key)];

        public void Dispose()
        {
            foreach (var handle in Handles.Values)
            {
                handle?.Dispose();
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Resuming the interrupted scan of library {LibraryId}")]
    private static partial void LogResuming(ILogger logger, Guid libraryId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The folder {Root} of library {LibraryId} can't be watched; its changes are picked up by the periodic scan")]
    private static partial void LogWatchFailed(ILogger logger, Exception exception, string root, Guid libraryId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Checking for due library scans failed; trying again shortly")]
    private static partial void LogTickFailed(ILogger logger, Exception exception);
}
