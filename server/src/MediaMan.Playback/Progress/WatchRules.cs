namespace MediaMan.Playback.Progress;

/// <summary>A profile's progress on a Playable as the rules record it.</summary>
public readonly record struct RecordedProgress(double PositionSeconds, double DurationSeconds, bool Watched);

/// <summary>An episode by its place in a show: season and episode number.</summary>
public readonly record struct EpisodeSlot(int SeasonNumber, int EpisodeNumber);

/// <summary>
/// Resume, watched and next-episode rules (SPEC §7.4 steps 4–5, §8 playback [M]: resume after 2 minutes; watched at
/// 90%; "Up next").
/// </summary>
public static class WatchRules
{
    public const double ResumeAfterSeconds = 120;
    public const double WatchedFraction = 0.9;
    public const int SpecialsSeason = 0;

    /// <summary>
    /// The position to offer resuming from: only after 2 minutes, and not once 90% is reached (that counts as
    /// watched, so it starts over). Null when resume isn't offered. An unknown duration (0) doesn't block resume.
    /// </summary>
    public static double? ResumeAt(double positionSeconds, double durationSeconds) =>
        positionSeconds >= ResumeAfterSeconds && (durationSeconds <= 0 || positionSeconds < durationSeconds * WatchedFraction)
            ? positionSeconds
            : null;

    /// <summary>
    /// The progress to store for a report: an unknown duration (0) keeps the one known from earlier
    /// (<paramref name="previous"/>), a position past a known end counts as the end, 90% of a known duration marks it
    /// watched, and once watched it stays watched.
    /// </summary>
    public static RecordedProgress Record(RecordedProgress? previous, double positionSeconds, double durationSeconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(positionSeconds);
        ArgumentOutOfRangeException.ThrowIfNegative(durationSeconds);
        if (durationSeconds <= 0 && previous is { DurationSeconds: > 0 } known)
        {
            durationSeconds = known.DurationSeconds;
        }

        var wasWatched = previous?.Watched ?? false;
        var position = durationSeconds > 0 ? Math.Min(positionSeconds, durationSeconds) : positionSeconds;
        var reached = durationSeconds > 0 && position >= durationSeconds * WatchedFraction;
        return new RecordedProgress(position, durationSeconds, wasWatched || reached);
    }

    /// <summary>
    /// The episode after <paramref name="current"/> among <paramref name="candidates"/> (the show's episodes with a
    /// file to play): the next number in its season, else the first episode of the next season that isn't specials.
    /// Null when there is none.
    /// </summary>
    public static EpisodeSlot? NextEpisode(EpisodeSlot current, IEnumerable<EpisodeSlot> candidates)
    {
        var ordered = candidates.Distinct()
            .OrderBy(c => c.SeasonNumber)
            .ThenBy(c => c.EpisodeNumber)
            .ToList();

        foreach (var c in ordered)
        {
            if (c.SeasonNumber == current.SeasonNumber && c.EpisodeNumber > current.EpisodeNumber)
            {
                return c;
            }
        }

        foreach (var c in ordered)
        {
            if (c.SeasonNumber > current.SeasonNumber && c.SeasonNumber != SpecialsSeason)
            {
                return c;
            }
        }

        return null;
    }
}
