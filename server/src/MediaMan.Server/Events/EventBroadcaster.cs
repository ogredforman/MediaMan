using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using MediaMan.Core;

namespace MediaMan.Server.Events;

/// <summary>Fans published events out to every open SSE subscriber.</summary>
public sealed class EventBroadcaster(IClock clock)
{
    // A subscriber that falls this far behind loses its oldest events rather than growing without bound.
    private const int SubscriberBuffer = 256;

    private readonly ConcurrentDictionary<Channel<EventEnvelope>, byte> _subscribers = new();
    private long _nextId;

    public int SubscriberCount => _subscribers.Count;

    public EventEnvelope Publish(string type, object data)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentNullException.ThrowIfNull(data);

        var id = Interlocked.Increment(ref _nextId).ToString(CultureInfo.InvariantCulture);
        var envelope = new EventEnvelope(id, type, clock.UtcNow, data);
        foreach (var channel in _subscribers.Keys)
        {
            channel.Writer.TryWrite(envelope);
        }

        return envelope;
    }

    /// <summary>Registers the subscriber immediately; events are yielded until <paramref name="ct"/> is cancelled.</summary>
    public IAsyncEnumerable<EventEnvelope> Subscribe(CancellationToken ct)
    {
        var channel = Channel.CreateBounded<EventEnvelope>(new BoundedChannelOptions(SubscriberBuffer)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });
        _subscribers.TryAdd(channel, 0);
        return ReadAsync(channel, ct);
    }

    private async IAsyncEnumerable<EventEnvelope> ReadAsync(
        Channel<EventEnvelope> channel,
        [EnumeratorCancellation] CancellationToken ct)
    {
        try
        {
            await foreach (var envelope in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                yield return envelope;
            }
        }
        finally
        {
            _subscribers.TryRemove(channel, out _);
        }
    }
}
