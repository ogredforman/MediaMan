using System.Threading.Channels;
using MediaMan.Core;
using MediaMan.Core.Modules;
using MediaMan.Server.Events;
using MediaMan.Server.LibraryApi;

namespace MediaMan.Server.Scanning;

public enum ScanStatus
{
    Idle,
    Queued,
    Running,
}

public enum ScanPhase
{
    Scanning,
    Matching,
    Importing,
}

/// <summary>
/// Queues library scans for <see cref="ScanWorker"/> and holds each library's scan state in memory. Every change is
/// published as a <c>scan.progress</c> event (SPEC §7.2, §10.1).
/// </summary>
public sealed class ScanCoordinator(EventBroadcaster events, IClock clock, IServiceProviderIsService services)
{
    public const string ProgressEventType = "scan.progress";

    private static readonly ScanState _idle = new(Wire.Name(ScanStatus.Idle), null, null, null);

    private readonly Lock _lock = new();
    private readonly Dictionary<Guid, ScanState> _states = [];
    private readonly Channel<Guid> _queue = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions { SingleReader = true });

    /// <summary>Scanning needs a metadata provider and a media probe; until Stage 2 only the dev configuration registers them.</summary>
    public bool IsAvailable => services.IsService(typeof(IMetadataProvider)) && services.IsService(typeof(IMediaProbe));

    internal ChannelReader<Guid> Queue => _queue.Reader;

    public ScanState Get(Guid libraryId)
    {
        lock (_lock)
        {
            return _states.GetValueOrDefault(libraryId, _idle);
        }
    }

    /// <summary>Queues a scan unless one is already queued or running; either way returns the library's current state.</summary>
    public ScanState Request(Guid libraryId)
    {
        lock (_lock)
        {
            var current = _states.GetValueOrDefault(libraryId, _idle);
            if (current.State != Wire.Name(ScanStatus.Idle))
            {
                return current;
            }

            var queued = Set(libraryId, current with { State = Wire.Name(ScanStatus.Queued) });
            _queue.Writer.TryWrite(libraryId);
            return queued;
        }
    }

    internal void Start(Guid libraryId) => Update(libraryId, s => s with
    {
        State = Wire.Name(ScanStatus.Running),
        Phase = Wire.Name(ScanPhase.Scanning),
        StartedAt = clock.UtcNow,
    });

    internal void Enter(Guid libraryId, ScanPhase phase) => Update(libraryId, s => s with { Phase = Wire.Name(phase) });

    internal void Finish(Guid libraryId, bool succeeded, ScanCounts counts, ScanError? error) => Update(libraryId, s => new ScanState(
        Wire.Name(ScanStatus.Idle),
        null,
        null,
        new ScanSummary(s.StartedAt ?? clock.UtcNow, clock.UtcNow, succeeded ? "succeeded" : "failed", counts, error)));

    private void Update(Guid libraryId, Func<ScanState, ScanState> change)
    {
        lock (_lock)
        {
            Set(libraryId, change(_states.GetValueOrDefault(libraryId, _idle)));
        }
    }

    // Called under the lock, so events go out in the order the states changed.
    private ScanState Set(Guid libraryId, ScanState state)
    {
        _states[libraryId] = state;
        events.Publish(ProgressEventType, new ScanProgressEvent(libraryId, state));
        return state;
    }
}
