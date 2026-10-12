using MediaMan.Core;
using MediaMan.Core.Domain;
using MediaMan.Data;
using Microsoft.EntityFrameworkCore;

namespace MediaMan.Server.PlaybackApi;

public enum WatchedFailure
{
    TitleNotFound,
    SeasonNotFound,
}

/// <summary>
/// Mark watched and unwatched (SPEC §8 browsing): sets the profile's <see cref="WatchState"/> on Playables directly,
/// outside playback. Writes watch states only, never anything on disk.
/// </summary>
public sealed class WatchedService(MediaManDbContext db, IClock clock)
{
    /// <summary>Marks one movie or episode; false when the Playable doesn't exist.</summary>
    public async Task<bool> SetPlayableAsync(Guid profileId, Guid playableId, bool watched, CancellationToken ct)
    {
        if (!await db.Playables.AnyAsync(p => p.Id == playableId, ct))
        {
            return false;
        }

        await SetAsync(profileId, [playableId], watched, ct);
        return true;
    }

    /// <summary>Marks every Playable of the title, or of one of its seasons; null on success.</summary>
    public async Task<WatchedFailure?> SetTitleAsync(Guid profileId, Guid titleId, int? seasonNumber, bool watched, CancellationToken ct)
    {
        if (!await db.Titles.AnyAsync(t => t.Id == titleId, ct))
        {
            return WatchedFailure.TitleNotFound;
        }

        if (seasonNumber is { } number && !await db.Seasons.AnyAsync(s => s.TitleId == titleId && s.Number == number, ct))
        {
            return WatchedFailure.SeasonNotFound;
        }

        List<Guid> movies = seasonNumber is null
            ? await db.Playables.AsNoTracking().Where(p => p.TitleId == titleId).OrderBy(p => p.Id).Select(p => p.Id).ToListAsync(ct)
            : [];
        var episodes = await (
            from season in db.Seasons
            where season.TitleId == titleId && (seasonNumber == null || season.Number == seasonNumber)
            join episode in db.Episodes on season.Id equals episode.SeasonId
            join playable in db.Playables on (Guid?)episode.Id equals playable.EpisodeId
            orderby season.Number, episode.Number, playable.Id
            select playable.Id)
            .ToListAsync(ct);

        await SetAsync(profileId, [.. movies, .. episodes], watched, ct);
        return null;
    }

    // Watched drops the resume position and stamps the Playables in order, a millisecond apart and the last one now, so
    // Next Up follows the last episode marked. Unwatched removes the state, so the Playable starts fresh.
    private async Task SetAsync(Guid profileId, IReadOnlyList<Guid> playableIds, bool watched, CancellationToken ct)
    {
        var states = await db.WatchStates
            .Where(w => w.ProfileId == profileId && playableIds.Contains(w.PlayableId))
            .ToDictionaryAsync(w => w.PlayableId, ct);

        if (!watched)
        {
            db.WatchStates.RemoveRange(states.Values);
        }
        else
        {
            var now = clock.UtcNow;
            for (var i = 0; i < playableIds.Count; i++)
            {
                var playableId = playableIds[i];
                if (!states.TryGetValue(playableId, out var state))
                {
                    state = new WatchState { ProfileId = profileId, PlayableId = playableId };
                    db.WatchStates.Add(state);
                }

                state.PositionSeconds = 0;
                state.Watched = true;
                state.LastWatchedAt = now.AddMilliseconds(i - (playableIds.Count - 1));
            }
        }

        await db.SaveChangesAsync(ct);
    }
}
