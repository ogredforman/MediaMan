namespace MediaMan.Core;

/// <summary>Entity ids are time-ordered UUIDv7s stamped from <see cref="IClock"/> (ADR 0002).</summary>
public static class Ids
{
    public static Guid New(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        return Guid.CreateVersion7(clock.UtcNow);
    }
}
