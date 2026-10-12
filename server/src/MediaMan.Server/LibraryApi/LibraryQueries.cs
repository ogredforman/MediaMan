using MediaMan.Auth;
using MediaMan.Core.Domain;
using MediaMan.Data;
using MediaMan.Playback.Progress;
using MediaMan.Playback.Versions;
using MediaMan.Server.Scanning;
using Microsoft.EntityFrameworkCore;

namespace MediaMan.Server.LibraryApi;

/// <summary>Read-only queries behind the library endpoints. Writes nothing.</summary>
public sealed class LibraryQueries(MediaManDbContext db, ScanCoordinator scans, UrlSigner urls)
{
    // Names sort case-insensitively, so "alien" files next to "Aliens".
    private const string NameCollation = "NOCASE";

    // Every NeedsAttention file should have a reason; this covers rows written before reasons were recorded.
    private const string UnknownReason = "unknown";

    // Search candidates checked for availability per query; enough that one round usually fills the page.
    private const int SearchChunk = 100;

    public async Task<LibraryPage> ListLibrariesAsync(PageRequest page, CancellationToken ct)
    {
        var rows = await db.Libraries.AsNoTracking()
            .OrderBy(l => EF.Functions.Collate(l.Name, NameCollation))
            .ThenBy(l => l.Id)
            .Skip(page.Offset)
            .Take(page.Limit + 1)
            .ToListAsync(ct);

        var (items, info) = Paging.Page(rows, page);
        return new LibraryPage(
            [.. items.Select(ToDto)],
            info);
    }

    /// <summary>The library with its scan state, or null when it doesn't exist.</summary>
    public async Task<LibraryDto?> GetLibraryAsync(Guid libraryId, CancellationToken ct)
    {
        var library = await db.Libraries.AsNoTracking().FirstOrDefaultAsync(l => l.Id == libraryId, ct);
        return library is null ? null : ToDto(library);
    }

    /// <summary>The library's NeedsAttention files with their MatchReviews, sorted by path; null when the library doesn't exist.</summary>
    public async Task<AttentionFilePage?> ListAttentionAsync(Guid libraryId, PageRequest page, CancellationToken ct)
    {
        if (!await db.Libraries.AnyAsync(l => l.Id == libraryId, ct))
        {
            return null;
        }

        var rows = await db.MediaFiles.AsNoTracking()
            .Where(f => f.LibraryId == libraryId && f.Status == MediaFileStatus.NeedsAttention)
            .OrderBy(f => f.Path)
            .ThenBy(f => f.Id)
            .Skip(page.Offset)
            .Take(page.Limit + 1)
            .ToListAsync(ct);

        var (items, info) = Paging.Page(rows, page);
        var ids = items.Select(f => f.Id).ToList();
        var reviews = await db.MatchReviews.AsNoTracking().Where(r => ids.Contains(r.MediaFileId)).ToListAsync(ct);
        var reviewByFile = reviews.ToDictionary(r => r.MediaFileId);

        return new AttentionFilePage(
            [.. items.Select(f => new AttentionFile(
                f.Id,
                f.Path,
                Path.GetFileName(f.Path),
                f.AttentionReason ?? UnknownReason,
                ToGuess(f.Parsed),
                ToReview(reviewByFile.GetValueOrDefault(f.Id))))],
            info);
    }

    private LibraryDto ToDto(Core.Domain.Library l) =>
        new(l.Id, l.Name, Wire.Name(l.Kind), Wire.Name(l.ContentType), l.RootPaths, l.ScanIntervalMinutes, scans.Get(l.Id));

    private static ParsedGuess? ToGuess(ParsedRelease? p) => p is null
        ? null
        : new ParsedGuess(p.Title, p.Year, p.Season, p.Episodes, p.AbsoluteEpisode, ResolutionName(p.Resolution),
            p.Source, p.Codec, p.HdrFormat, p.Audio, p.Group);

    private static string? ResolutionName(Resolution? resolution) => resolution switch
    {
        null => null,
        Resolution.P720 => "720p",
        Resolution.P1080 => "1080p",
        Resolution.P1440 => "1440p",
        Resolution.P2160 => "2160p",
        _ => throw new ArgumentOutOfRangeException(nameof(resolution), resolution, "Add the contract's name for this resolution."),
    };

    private static MatchReviewDto? ToReview(MatchReview? review) => review is null
        ? null
        : new MatchReviewDto(review.Id, review.Reason, [.. review.Candidates.Select(c => new MatchCandidateDto(
            c.TitleRef.ToString(), c.Score, c.Kind is { } k ? Wire.Name(k) : null, c.Name, c.Year, c.PosterUrl))]);

    /// <summary>Titles with at least one Matched file (optionally in <paramref name="libraryId"/>), sorted by name.</summary>
    public async Task<TitlePage> ListTitlesAsync(TitleKind? kind, Guid? libraryId, PageRequest page, Guid? profileId, CancellationToken ct)
    {
        var titles = AvailableTitles(libraryId);
        if (kind is { } k)
        {
            titles = titles.Where(t => t.Kind == k);
        }

        var rows = await titles
            .OrderBy(t => EF.Functions.Collate(t.Name, NameCollation))
            .ThenBy(t => t.Year)
            .ThenBy(t => t.Id)
            .Skip(page.Offset)
            .Take(page.Limit + 1)
            .ToListAsync(ct);

        var (items, info) = Paging.Page(rows, page);
        return new TitlePage(await SummariesAsync(items, profileId, ct), info);
    }

    /// <summary>
    /// Available titles whose name matches the query by <see cref="SearchRules.Rank"/>, best first, then by name and year.
    /// Names are matched in memory so accents and punctuation fold the same way everywhere; SQLite's LIKE folds ASCII only.
    /// </summary>
    public async Task<LibrarySearchResults> SearchTitlesAsync(string q, int limit, Guid? profileId, CancellationToken ct)
    {
        var query = SearchRules.Query.Of(q);
        if (query.IsBlank)
        {
            return new LibrarySearchResults(q, []);
        }

        // Rank every name first (a scan of Titles alone), then check availability only for the best candidates, a chunk at
        // a time: checking it for every title on every keystroke was the slow part at 20,000 files.
        var names = await db.Titles.AsNoTracking().Select(t => new { t.Id, t.Name, t.Year }).ToListAsync(ct);
        var ranked = names
            .Select(t => (t.Id, t.Name, t.Year, Rank: SearchRules.Rank(query, t.Name)))
            .Where(t => t.Rank is not null)
            .OrderBy(t => t.Rank)
            .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(t => t.Year)
            .ThenBy(t => t.Id)
            .Select(t => t.Id)
            .ToList();

        var picked = new List<Guid>(limit);
        foreach (var chunk in ranked.Chunk(SearchChunk))
        {
            var available = (await AvailableTitles(libraryId: null).Where(t => chunk.Contains(t.Id)).Select(t => t.Id).ToListAsync(ct)).ToHashSet();
            picked.AddRange(chunk.Where(available.Contains).Take(limit - picked.Count));
            if (picked.Count == limit)
            {
                break;
            }
        }

        var rows = await db.Titles.AsNoTracking().Where(t => picked.Contains(t.Id)).ToListAsync(ct);
        var byId = rows.ToDictionary(t => t.Id);
        return new LibrarySearchResults(q, await SummariesAsync([.. picked.Select(id => byId[id])], profileId, ct));
    }

    private async Task<IReadOnlyList<TitleSummary>> SummariesAsync(IReadOnlyList<Title> titles, Guid? profileId, CancellationToken ct)
    {
        var ids = titles.Select(t => t.Id).ToList();
        var posters = await db.Artwork.AsNoTracking()
            .Where(a => a.TitleId != null && ids.Contains(a.TitleId.Value) && a.Type == ArtworkType.Poster)
            .OrderBy(a => a.Id)
            .ToListAsync(ct);
        var posterByTitle = posters.GroupBy(a => a.TitleId!.Value).ToDictionary(g => g.Key, g => g.First());
        var watched = await WatchedTitlesAsync(ids, profileId, ct);

        return [.. titles.Select(t => new TitleSummary(
            t.Id, Wire.Name(t.Kind), t.Name, t.Year, t.Rating, t.Genres, Ref(posterByTitle.GetValueOrDefault(t.Id)), watched.Contains(t.Id)))];
    }

    /// <summary>
    /// The titles among <paramref name="titleIds"/> the profile watched, by <see cref="WatchRules.TitleWatched"/> over
    /// their Playables with a matched file. Empty without a picked profile.
    /// </summary>
    private async Task<HashSet<Guid>> WatchedTitlesAsync(List<Guid> titleIds, Guid? profileId, CancellationToken ct)
    {
        if (profileId is not { } profile || titleIds.Count == 0)
        {
            return [];
        }

        var watched = (await db.WatchStates.AsNoTracking()
            .Where(w => w.ProfileId == profile && w.Watched)
            .Select(w => w.PlayableId)
            .ToListAsync(ct)).ToHashSet();
        if (watched.Count == 0)
        {
            return [];
        }

        var matched = db.Playables.AsNoTracking().Where(p => db.FileLinks.Any(l =>
            l.PlayableId == p.Id && db.MediaFiles.Any(f => f.Id == l.MediaFileId && f.Status == MediaFileStatus.Matched)));
        var movies = await matched
            .Where(p => p.TitleId != null && titleIds.Contains(p.TitleId.Value))
            .Select(p => new { p.Id, TitleId = p.TitleId!.Value, SeasonNumber = (int?)null })
            .ToListAsync(ct);
        var episodes = await (
            from playable in matched
            join episode in db.Episodes on playable.EpisodeId equals (Guid?)episode.Id
            join season in db.Seasons on episode.SeasonId equals season.Id
            where titleIds.Contains(season.TitleId)
            select new { playable.Id, season.TitleId, SeasonNumber = (int?)season.Number })
            .ToListAsync(ct);

        return [.. movies.Concat(episodes)
            .GroupBy(p => p.TitleId)
            .Where(g => WatchRules.TitleWatched([.. g.Select(p => new PlayableWatch(p.SeasonNumber, watched.Contains(p.Id)))]))
            .Select(g => g.Key)];
    }

    // Titles with at least one matched file (in the library, when given): what browsing and search show.
    private IQueryable<Title> AvailableTitles(Guid? libraryId)
    {
        var files = db.MediaFiles.Where(f => f.Status == MediaFileStatus.Matched);
        if (libraryId is { } library)
        {
            files = files.Where(f => f.LibraryId == library);
        }

        // Per-title EXISTS walks the TitleId/PlayableId indexes; an IN over every matched file's title was ~10x slower at 20,000 files.
        var linked = db.Playables.Where(p => db.FileLinks.Any(l => l.PlayableId == p.Id && files.Any(f => f.Id == l.MediaFileId)));
        return db.Titles.AsNoTracking().Where(t =>
            linked.Any(p => p.TitleId == t.Id)
            || db.Seasons.Any(s => s.TitleId == t.Id && db.Episodes.Any(e => e.SeasonId == s.Id && linked.Any(p => p.EpisodeId == e.Id))));
    }

    /// <summary>The title's detail, or null when it doesn't exist.</summary>
    public async Task<TitleDetail?> GetTitleAsync(Guid titleId, Guid? profileId, CancellationToken ct)
    {
        var title = await db.Titles.AsNoTracking().FirstOrDefaultAsync(t => t.Id == titleId, ct);
        if (title is null)
        {
            return null;
        }

        var preference = profileId is { } profile
            ? await db.VersionPreferences.AsNoTracking().FirstOrDefaultAsync(v => v.ProfileId == profile && v.TitleId == titleId, ct)
            : null;

        var artwork = await db.Artwork.AsNoTracking().Where(a => a.TitleId == titleId).OrderBy(a => a.Id).ToListAsync(ct);
        ArtworkRef? Art(ArtworkType type) => Ref(artwork.FirstOrDefault(a => a.Type == type));

        MoviePlayable? movie = null;
        IReadOnlyList<SeasonDto>? seasons = null;
        if (title.Kind == TitleKind.Movie)
        {
            movie = await MoviePlayableAsync(titleId, profileId, preference, ct);
        }
        else
        {
            seasons = await SeasonsAsync(titleId, profileId, preference, ct);
        }

        var ext = title.ExternalIds;
        return new TitleDetail(
            title.Id, Wire.Name(title.Kind), title.Name, title.Year, title.Rating, title.Genres, Art(ArtworkType.Poster),
            title.Overview, title.RuntimeMinutes, new ExternalIdsDto(ext.Tmdb, ext.Imdb, ext.Tvdb, ext.AniList),
            Art(ArtworkType.Backdrop), Art(ArtworkType.Logo), movie, seasons,
            preference is null ? null : new VersionPreferenceDto(preference.MediaFileId, preference.Resolution, preference.HdrFormat));
    }

    private async Task<MoviePlayable?> MoviePlayableAsync(
        Guid titleId, Guid? profileId, VersionPreference? preference, CancellationToken ct)
    {
        var playable = await db.Playables.AsNoTracking().FirstOrDefaultAsync(p => p.TitleId == titleId, ct);
        if (playable is null)
        {
            return null;
        }

        var versions = await VersionsAsync([playable.Id], preference, ct);
        var found = versions.GetValueOrDefault(playable.Id);
        var watch = WatchOf(await WatchStatesAsync(profileId, [playable.Id], ct), playable.Id, found);
        return new MoviePlayable(playable.Id, found?.Versions ?? [], found?.Selected, watch.ResumeSeconds, watch.Watched);
    }

    private async Task<IReadOnlyList<SeasonDto>> SeasonsAsync(
        Guid titleId, Guid? profileId, VersionPreference? preference, CancellationToken ct)
    {
        var seasons = await db.Seasons.AsNoTracking().Where(s => s.TitleId == titleId).OrderBy(s => s.Number).ToListAsync(ct);
        var seasonIds = seasons.Select(s => s.Id).ToList();
        var episodes = await db.Episodes.AsNoTracking()
            .Where(e => seasonIds.Contains(e.SeasonId))
            .OrderBy(e => e.Number)
            .ToListAsync(ct);
        var episodeIds = episodes.Select(e => e.Id).ToList();
        var playables = await db.Playables.AsNoTracking()
            .Where(p => p.EpisodeId != null && episodeIds.Contains(p.EpisodeId.Value))
            .ToListAsync(ct);
        var artwork = await db.Artwork.AsNoTracking()
            .Where(a => (a.SeasonId != null && seasonIds.Contains(a.SeasonId.Value))
                || (a.EpisodeId != null && episodeIds.Contains(a.EpisodeId.Value)))
            .OrderBy(a => a.Id)
            .ToListAsync(ct);
        var versions = await VersionsAsync([.. playables.Select(p => p.Id)], preference, ct);
        var watches = await WatchStatesAsync(profileId, [.. playables.Select(p => p.Id)], ct);

        var playableByEpisode = playables.ToDictionary(p => p.EpisodeId!.Value, p => p.Id);
        var posterBySeason = artwork.Where(a => a.SeasonId != null && a.Type == ArtworkType.Poster)
            .GroupBy(a => a.SeasonId!.Value).ToDictionary(g => g.Key, g => g.First());
        var stillByEpisode = artwork.Where(a => a.EpisodeId != null && a.Type == ArtworkType.Still)
            .GroupBy(a => a.EpisodeId!.Value).ToDictionary(g => g.Key, g => g.First());
        var episodesBySeason = episodes.ToLookup(e => e.SeasonId);

        return [.. seasons.Select(s => new SeasonDto(
            s.Id, s.Number, s.Name, s.Overview, Ref(posterBySeason.GetValueOrDefault(s.Id)),
            [.. episodesBySeason[s.Id].Select(e =>
            {
                Guid? playableId = playableByEpisode.TryGetValue(e.Id, out var id) ? id : null;
                var found = playableId is { } p ? versions.GetValueOrDefault(p) : null;
                var watch = playableId is { } w ? WatchOf(watches, w, found) : default;
                return new EpisodeDto(
                    e.Id, e.Number, e.AbsoluteNumber, e.Name, e.Overview, e.AirDate, Ref(stillByEpisode.GetValueOrDefault(e.Id)),
                    playableId, found?.Versions ?? [], found?.Selected, watch.ResumeSeconds, watch.Watched);
            })]))];
    }

    /// <summary>
    /// Versions per Playable: linked files that are Matched or Missing. The scanner purges Missing files after
    /// their retention (SPEC §7.2 step 6), so a remounted drive brings its versions back. Best picture first, with the
    /// one Play uses for the profile's <paramref name="preference"/> (<see cref="VersionPicker"/>).
    /// </summary>
    private async Task<Dictionary<Guid, PlayableVersions>> VersionsAsync(
        List<Guid> playableIds, VersionPreference? preference, CancellationToken ct)
    {
        var rows = await (
                from link in db.FileLinks
                join file in db.MediaFiles on link.MediaFileId equals file.Id
                where playableIds.Contains(link.PlayableId)
                    && (file.Status == MediaFileStatus.Matched || file.Status == MediaFileStatus.Missing)
                select new { link.PlayableId, link.Part, File = file })
            .AsNoTracking()
            .ToListAsync(ct);

        return rows
            .GroupBy(r => r.PlayableId)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var picked = VersionPicker.Pick(g.Select(r => r.File), preference);
                    return new PlayableVersions(
                        [.. g
                            .Select(r => ToVersion(r.File, r.Part))
                            .OrderBy(v => v.Status == Wire.Name(MediaFileStatus.Missing))
                            .ThenByDescending(v => v.Height ?? 0)
                            .ThenBy(v => v.FileName, StringComparer.OrdinalIgnoreCase)],
                        picked?.Id,
                        picked?.Probe?.Duration?.TotalSeconds);
                });
    }

    private sealed record PlayableVersions(IReadOnlyList<MediaVersion> Versions, Guid? Selected, double? SelectedDurationSeconds);

    /// <summary>The profile's watch state per Playable; empty without a picked profile.</summary>
    private async Task<Dictionary<Guid, WatchState>> WatchStatesAsync(Guid? profileId, List<Guid> playableIds, CancellationToken ct) =>
        profileId is { } profile
            ? await db.WatchStates.AsNoTracking()
                .Where(w => w.ProfileId == profile && playableIds.Contains(w.PlayableId))
                .ToDictionaryAsync(w => w.PlayableId, ct)
            : [];

    /// <summary>
    /// Resume and watched as a playback session gives them (SPEC §7.4 step 2): the recorded duration, or the version
    /// Play uses when the player didn't know it.
    /// </summary>
    private static (double? ResumeSeconds, bool Watched) WatchOf(
        Dictionary<Guid, WatchState> watches, Guid playableId, PlayableVersions? versions) =>
        watches.TryGetValue(playableId, out var w)
            ? (WatchRules.ResumeAt(w.PositionSeconds, w.DurationSeconds > 0 ? w.DurationSeconds : versions?.SelectedDurationSeconds ?? 0), w.Watched)
            : (null, false);

    private static MediaVersion ToVersion(MediaFile file, int part)
    {
        var probe = file.Probe;
        var streams = probe?.Streams ?? [];
        // Cover art shows up as a tiny video stream, so the main picture is the largest one.
        var video = streams.Where(s => s.Type == MediaStreamType.Video).MaxBy(s => (long)(s.Width ?? 0) * (s.Height ?? 0));
        var audio = streams.Where(s => s.Type == MediaStreamType.Audio).OrderByDescending(s => s.IsDefault).FirstOrDefault();

        return new MediaVersion(
            file.Id,
            Path.GetFileName(file.Path),
            file.SizeBytes,
            Wire.Name(file.Status),
            part,
            probe?.Container,
            probe?.Duration?.TotalSeconds,
            video?.Width,
            video?.Height,
            video?.Codec,
            probe?.HdrFormat ?? file.Parsed?.HdrFormat,
            audio?.Codec,
            audio?.Channels,
            file.Parsed?.Source,
            file.Parsed?.Group);
    }

    private ArtworkRef? Ref(Artwork? artwork) => artwork is null ? null : new ArtworkRef(urls.SignedUrlOf(artwork.Id), artwork.DominantColor);
}
