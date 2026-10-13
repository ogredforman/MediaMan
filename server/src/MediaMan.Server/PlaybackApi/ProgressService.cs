using MediaMan.Core;
using MediaMan.Core.Domain;
using MediaMan.Data;
using MediaMan.Playback.Progress;
using Microsoft.EntityFrameworkCore;

namespace MediaMan.Server.PlaybackApi;

/// <summary>
/// Watch progress (SPEC §7.4 steps 4–5): stores a profile's position on a Playable by <see cref="WatchRules"/> and
/// finds the next episode with a file to play. Keyed by Playable and profile, so it needs no playback session.
/// </summary>
public sealed class ProgressService(MediaManDbContext db, PlaybackService playback, IClock clock)
{
    /// <summary>Records the report for <paramref name="profileId"/>; null when the Playable doesn't exist.</summary>
    public async Task<WatchProgressDto?> ReportAsync(Guid playableId, Guid profileId, double positionSeconds, double durationSeconds, CancellationToken ct)
    {
        var playable = await db.Playables.AsNoTracking().SingleOrDefaultAsync(p => p.Id == playableId, ct);
        if (playable is null)
        {
            return null;
        }

        var state = await db.WatchStates.SingleOrDefaultAsync(w => w.ProfileId == profileId && w.PlayableId == playableId, ct);
        var previous = state is null ? (RecordedProgress?)null : new RecordedProgress(state.PositionSeconds, state.DurationSeconds, state.Watched);
        if (state is null)
        {
            state = new WatchState { ProfileId = profileId, PlayableId = playableId };
            db.WatchStates.Add(state);
        }

        var recorded = WatchRules.Record(previous, positionSeconds, durationSeconds);
        state.PositionSeconds = recorded.PositionSeconds;
        state.DurationSeconds = recorded.DurationSeconds;
        state.Watched = recorded.Watched;
        state.LastWatchedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);

        return new WatchProgressDto(playableId, recorded.PositionSeconds, recorded.DurationSeconds, recorded.Watched,
            playable.EpisodeId is { } episodeId ? await playback.NextEpisodeAsync(episodeId, ct) : null);
    }
}
