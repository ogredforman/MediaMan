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
            playable.EpisodeId is { } episodeId ? await NextEpisodeAsync(episodeId, ct) : null);
    }

    private async Task<NextEpisodeDto?> NextEpisodeAsync(Guid episodeId, CancellationToken ct)
    {
        var current = await db.Episodes.AsNoTracking()
            .Where(e => e.Id == episodeId)
            .Join(db.Seasons.AsNoTracking(), e => e.SeasonId, s => s.Id, (e, s) => new { s.TitleId, Slot = new EpisodeSlot(s.Number, e.Number) })
            .SingleAsync(ct);

        // The show's episodes that hold a file not marked missing; the disk check below drops ones that are gone.
        var linked = await db.Seasons.AsNoTracking()
            .Where(s => s.TitleId == current.TitleId)
            .Join(db.Episodes.AsNoTracking(), s => s.Id, e => e.SeasonId, (s, e) => new { s.Number, Episode = e })
            .Join(db.Playables.AsNoTracking(), x => x.Episode.Id, p => p.EpisodeId, (x, p) => new { x.Number, EpisodeNumber = x.Episode.Number, PlayableId = p.Id })
            .Join(db.FileLinks.AsNoTracking(), x => x.PlayableId, l => l.PlayableId, (x, l) => new { x.Number, x.EpisodeNumber, x.PlayableId, l.MediaFileId })
            .Join(db.MediaFiles.AsNoTracking().Where(f => f.Status != MediaFileStatus.Missing), x => x.MediaFileId, f => f.Id,
                (x, f) => new { x.Number, x.EpisodeNumber, x.PlayableId, f.Path })
            .ToListAsync(ct);
        var candidates = linked
            .Where(x => File.Exists(x.Path))
            .GroupBy(x => new EpisodeSlot(x.Number, x.EpisodeNumber))
            .ToDictionary(g => g.Key, g => g.First().PlayableId);

        if (WatchRules.NextEpisode(current.Slot, candidates.Keys) is not { } next)
        {
            return null;
        }

        var nextPlayable = await db.Playables.AsNoTracking().SingleAsync(p => p.Id == candidates[next], ct);
        return new NextEpisodeDto(nextPlayable.Id, next.SeasonNumber, next.EpisodeNumber, await playback.DisplayNameAsync(nextPlayable, ct));
    }
}
