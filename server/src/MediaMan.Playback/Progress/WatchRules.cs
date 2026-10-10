namespace MediaMan.Playback.Progress;

/// <summary>When resume is offered (SPEC §8 playback [M]: resume after 2 minutes; watched at 90%).</summary>
public static class WatchRules
{
    public const double ResumeAfterSeconds = 120;
    public const double WatchedFraction = 0.9;

    /// <summary>
    /// The position to offer resuming from: only after 2 minutes, and not once 90% is reached (that counts as
    /// watched, so it starts over). Null when resume isn't offered. An unknown duration (0) doesn't block resume.
    /// </summary>
    public static double? ResumeAt(double positionSeconds, double durationSeconds) =>
        positionSeconds >= ResumeAfterSeconds && (durationSeconds <= 0 || positionSeconds < durationSeconds * WatchedFraction)
            ? positionSeconds
            : null;
}
