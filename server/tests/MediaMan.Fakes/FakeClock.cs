using MediaMan.Core;

namespace MediaMan.Fakes;

/// <summary>A settable clock. Time-dependent tests advance it instead of sleeping (SPEC §10.4).</summary>
public sealed class FakeClock : IClock
{
    /// <summary>The default start time, so tests are reproducible without setting one.</summary>
    public static readonly DateTimeOffset DefaultStart = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private DateTimeOffset _utcNow;

    public FakeClock()
        : this(DefaultStart)
    {
    }

    public FakeClock(DateTimeOffset start)
    {
        UtcNow = start;
    }

    public DateTimeOffset UtcNow
    {
        get => _utcNow;
        set => _utcNow = value.ToUniversalTime();
    }

    public void Advance(TimeSpan by)
    {
        if (by < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(by), by, "A clock only moves forward; set UtcNow to go back.");
        }

        UtcNow += by;
    }
}
