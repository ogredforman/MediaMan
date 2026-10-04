namespace MediaMan.Core;

/// <summary>The only source of time in server code (SPEC §10.4).</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

/// <summary>Real clock. The one place in <c>server/src</c> allowed to read system time.</summary>
public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
