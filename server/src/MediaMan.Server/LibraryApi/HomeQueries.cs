using MediaMan.Auth;
using MediaMan.Core;
using MediaMan.Core.Domain;
using MediaMan.Data;
using MediaMan.Playback.Progress;
using Microsoft.EntityFrameworkCore;

namespace MediaMan.Server.LibraryApi;

/// <summary>Builds the Home screen from the library and the profile's watch states by <see cref="HomeRules"/>.</summary>
public sealed class HomeQueries(MediaManDbContext db, IClock clock, UrlSigner urls)
{
    /// <summary>The hero and rows; without a profile, no Continue Watching or Next Up.</summary>
    public async Task<HomeDto> GetAsync(Guid? profileId, CancellationToken ct)
    {
        // Every Playable with a matched file, as listTitles counts them; a multi-file Playable appears once per file.
        var files = db.MediaFiles.Where(f => f.Status == MediaFileStatus.Matched);
        var movies = await (
            from link in db.FileLinks
            join file in files on link.MediaFileId equals file.Id
            join playable in db.Playables on link.PlayableId equals playable.Id
            where playable.TitleId != null
            select new { playable.Id, TitleId = playable.TitleId!.Value, file.AddedAt })
            .AsNoTracking().ToListAsync(ct);
        var episodes = await (
            from link in db.FileLinks
            join file in files on link.MediaFileId equals file.Id
            join playable in db.Playables on link.PlayableId equals playable.Id
            join episode in db.Episodes on playable.EpisodeId equals (Guid?)episode.Id
            join season in db.Seasons on episode.SeasonId equals season.Id
            select new { playable.Id, season.TitleId, SeasonNumber = season.Number, EpisodeId = episode.Id, episode.Number, episode.Name, file.AddedAt })
            .AsNoTracking().ToListAsync(ct);

        List<HomePlayable> playables =
        [
            .. movies.DistinctBy(m => m.Id).Select(m => new HomePlayable(m.Id, m.TitleId, null)),
            .. episodes.DistinctBy(e => e.Id).Select(e => new HomePlayable(e.Id, e.TitleId, new EpisodeSlot(e.SeasonNumber, e.Number))),
        ];
        var episodeByPlayable = episodes.DistinctBy(e => e.Id).ToDictionary(e => e.Id);
        var addedAt = movies.Select(m => (m.TitleId, m.AddedAt))
            .Concat(episodes.Select(e => (e.TitleId, e.AddedAt)))
            .GroupBy(x => x.TitleId)
            .ToDictionary(g => g.Key, g => g.Max(x => x.AddedAt));

        var titleIds = addedAt.Keys.ToList();
        var titles = await db.Titles.AsNoTracking().Where(t => titleIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, ct);
        var withBackdrop = (await db.Artwork.AsNoTracking()
            .Where(a => a.TitleId != null && a.Type == ArtworkType.Backdrop)
            .Select(a => a.TitleId!.Value)
            .Distinct()
            .ToListAsync(ct)).ToHashSet();
        var ranked = titles.Values
            .Select(t => new HomeTitle(t.Id, t.Name, t.Year, t.Rating, t.Genres, addedAt[t.Id], withBackdrop.Contains(t.Id)))
            .ToList();

        var watches = profileId is { } profile
            ? await db.WatchStates.AsNoTracking().Where(w => w.ProfileId == profile).ToListAsync(ct)
            : [];
        var watching = HomeRules.Watching(playables, watches);
        var continueWatching = watching.ContinueWatching.Take(HomeRules.RowSize).ToList();
        var nextUp = watching.NextUp.Take(HomeRules.RowSize).ToList();
        var recentlyAdded = HomeRules.RecentlyAdded(ranked);
        var genres = HomeRules.Genres(ranked);
        var hero = HomeRules.Hero(ranked, clock.UtcNow);
        var heroPlay = hero is null ? null : HomeRules.HeroPlay(hero.Id, playables.Where(p => p.TitleId == hero.Id), watching);

        // Artwork only for what the screen shows.
        var shownTitles = continueWatching.Concat(nextUp).Select(p => p.TitleId)
            .Concat(recentlyAdded.Select(t => t.Id))
            .Concat(genres.SelectMany(g => g.Titles).Select(t => t.Id))
            .Concat(hero is null ? [] : [hero.Id])
            .Distinct()
            .ToList();
        var shownEpisodes = continueWatching.Concat(nextUp).Concat(heroPlay is null ? [] : [heroPlay])
            .Select(p => episodeByPlayable.GetValueOrDefault(p.PlayableId)?.EpisodeId)
            .OfType<Guid>()
            .Distinct()
            .ToList();
        var titleArt = (await db.Artwork.AsNoTracking()
                .Where(a => a.TitleId != null && shownTitles.Contains(a.TitleId.Value))
                .OrderBy(a => a.Id)
                .ToListAsync(ct))
            .GroupBy(a => (a.TitleId!.Value, a.Type))
            .ToDictionary(g => g.Key, g => g.First());
        var stills = (await db.Artwork.AsNoTracking()
                .Where(a => a.EpisodeId != null && a.Type == ArtworkType.Still && shownEpisodes.Contains(a.EpisodeId.Value))
                .OrderBy(a => a.Id)
                .ToListAsync(ct))
            .GroupBy(a => a.EpisodeId!.Value)
            .ToDictionary(g => g.Key, g => g.First());

        ArtworkRef? TitleArt(Guid titleId, ArtworkType type) => Ref(titleArt.GetValueOrDefault((titleId, type)));

        TitleSummary Summary(Guid titleId)
        {
            var t = titles[titleId];
            return new TitleSummary(t.Id, Wire.Name(t.Kind), t.Name, t.Year, t.Rating, t.Genres, TitleArt(t.Id, ArtworkType.Poster));
        }

        HomeEpisode? EpisodeOf(Guid playableId) =>
            episodeByPlayable.GetValueOrDefault(playableId) is { } e
                ? new HomeEpisode(e.SeasonNumber, e.Number, e.Name, Ref(stills.GetValueOrDefault(e.EpisodeId)))
                : null;

        HomeItem WatchItem(WatchPick pick) => new(
            Summary(pick.TitleId),
            pick.PlayableId,
            EpisodeOf(pick.PlayableId),
            pick is { ResumeSeconds: { } position, DurationSeconds: { } duration } ? new HomeProgress(position, duration) : null);

        HomeItem TitleItem(HomeTitle title) => new(Summary(title.Id), null, null, null);

        List<HomeRow> rows =
        [
            new(HomeRow.ContinueWatching, null, [.. continueWatching.Select(WatchItem)]),
            new(HomeRow.NextUp, null, [.. nextUp.Select(WatchItem)]),
            new(HomeRow.RecentlyAdded, null, [.. recentlyAdded.Select(TitleItem)]),
            .. genres.Select(g => new HomeRow(HomeRow.GenreKind, g.Genre, [.. g.Titles.Select(TitleItem)])),
        ];

        var heroDto = hero is null
            ? null
            : new HomeHero(
                Summary(hero.Id),
                titles[hero.Id].Overview,
                TitleArt(hero.Id, ArtworkType.Backdrop),
                TitleArt(hero.Id, ArtworkType.Logo),
                heroPlay?.PlayableId,
                heroPlay is null ? null : EpisodeOf(heroPlay.PlayableId),
                heroPlay?.ResumeSeconds);

        return new HomeDto(heroDto, [.. rows.Where(r => r.Items.Count > 0)]);
    }

    private ArtworkRef? Ref(Artwork? artwork) => artwork is null ? null : new ArtworkRef(urls.SignedUrlOf(artwork.Id), artwork.DominantColor);
}
