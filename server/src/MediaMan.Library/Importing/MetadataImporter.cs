using MediaMan.Core;
using MediaMan.Core.Domain;
using MediaMan.Core.Modules;
using MediaMan.Data;
using MediaMan.Library.Matching;
using Microsoft.EntityFrameworkCore;

namespace MediaMan.Library.Importing;

/// <summary>One importer run: files linked, files sent to attention, and files left Pending because the provider failed.</summary>
public sealed record ImportResult(int Linked, int NeedsAttention, int Deferred);

/// <summary>
/// Turns <see cref="MatchDecision"/>s into stored metadata (SPEC §7.2 step 5, §8 metadata and multi-episode [P]).
/// It creates or reuses the Title by provider id, the Seasons that hold linked files with their full episode lists,
/// artwork refs (one per type per owner), and the Playables the file contains. Then it replaces the file's
/// FileLinks (one per episode, <see cref="FileLink.Part"/> from 1) and marks it Matched. Only missing metadata is
/// fetched: a stored title or season isn't asked for again. Provider errors leave the file Pending with nothing
/// written for it; an episode the provider doesn't have sends the file to attention.
/// </summary>
public sealed class MetadataImporter(MediaManDbContext db, IMetadataProvider provider, IClock clock, MatcherOptions options)
{
    public const string EpisodeNotFoundReason = "episode-not-found";

    public async Task<ImportResult> ImportAsync(IReadOnlyList<MatchDecision> decisions, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(decisions);
        var session = new ProviderSession(provider, options);
        int linked = 0, attention = 0, deferred = 0;
        foreach (var decision in decisions)
        {
            var file = await db.MediaFiles.SingleOrDefaultAsync(f => f.Id == decision.MediaFileId, ct);
            if (file is not { Status: MediaFileStatus.Pending })
            {
                // Removed or changed since the matcher ran; the next scan handles it.
                continue;
            }

            if (session.ProviderDown)
            {
                deferred++;
                continue;
            }

            List<Playable>? playables;
            try
            {
                playables = await ImportMetadataAsync(session, decision, ct);
            }
            catch (ProviderUnavailableException)
            {
                // Nothing for this file was staged: every provider call comes before the first Add.
                deferred++;
                continue;
            }

            if (playables is null)
            {
                file.Status = MediaFileStatus.NeedsAttention;
                file.AttentionReason = EpisodeNotFoundReason;
                await db.SaveChangesAsync(ct);
                attention++;
                continue;
            }

            var stale = await db.FileLinks.Where(l => l.MediaFileId == file.Id).ToListAsync(ct);
            db.FileLinks.RemoveRange(stale);
            for (var i = 0; i < playables.Count; i++)
            {
                db.FileLinks.Add(new FileLink { MediaFileId = file.Id, PlayableId = playables[i].Id, Part = i + 1 });
            }

            file.Status = MediaFileStatus.Matched;
            file.AttentionReason = null;

            // One save per file, so a cancelled run keeps the files already linked and leaves the rest Pending.
            await db.SaveChangesAsync(ct);
            linked++;
        }

        return new ImportResult(linked, attention, deferred);
    }

    /// <summary>Stages the title, seasons, episodes and artwork; returns the file's Playables in part order, or null if an episode isn't found.</summary>
    private async Task<List<Playable>?> ImportMetadataAsync(ProviderSession session, MatchDecision decision, CancellationToken ct)
    {
        var titleRef = decision.TitleRef;
        var title = await FindTitleAsync(titleRef, ct);
        var details = title is null ? await session.GetTitleAsync(titleRef, ct) : null;

        if (decision.Kind == TitleKind.Movie)
        {
            title = StageTitle(title, details!);
            return [await MoviePlayableAsync(title.Id, ct)];
        }

        var storedSeasons = title is null
            ? []
            : await db.Seasons.Where(s => s.TitleId == title.Id).ToDictionaryAsync(s => s.Number, ct);
        var storedEpisodes = storedSeasons.Count == 0
            ? []
            : await db.Episodes.Where(e => storedSeasons.Values.Select(s => s.Id).Contains(e.SeasonId)).ToListAsync(ct);

        // Resolve every target to (season, episode) and fetch the seasons the store doesn't fully have yet.
        var targets = new List<(int Season, int Episode)>();
        var fetched = new Dictionary<int, SeasonDetails>();
        if (decision.Episodes.Count > 0)
        {
            var seasonNumber = decision.Season ?? 1;
            targets.AddRange(decision.Episodes.Distinct().Select(e => (seasonNumber, e)));
            var stored = storedSeasons.GetValueOrDefault(seasonNumber);
            if (stored is null || !decision.Episodes.All(e => storedEpisodes.Any(x => x.SeasonId == stored.Id && x.Number == e)))
            {
                details ??= await session.GetTitleAsync(titleRef, ct);
                if (details.Seasons.All(s => s.Number != seasonNumber))
                {
                    return null;
                }

                fetched[seasonNumber] = await session.GetSeasonAsync(titleRef, seasonNumber, ct);
            }
        }
        else if (decision.AbsoluteEpisode is { } absolute)
        {
            var known = storedEpisodes.FirstOrDefault(e => e.AbsoluteNumber == absolute);
            if (known is not null)
            {
                targets.Add((storedSeasons.Values.Single(s => s.Id == known.SeasonId).Number, known.Number));
            }
            else
            {
                details ??= await session.GetTitleAsync(titleRef, ct);
                if (FindAbsolute(details, absolute) is not { } position)
                {
                    return null;
                }

                var season = await session.GetSeasonAsync(titleRef, position.Season, ct);
                var episode = season.Episodes.FirstOrDefault(e => e.AbsoluteNumber == absolute)
                    ?? season.Episodes.FirstOrDefault(e => e.Number == position.Offset);
                if (episode is null)
                {
                    return null;
                }

                fetched[position.Season] = season;
                targets.Add((position.Season, episode.Number));
            }
        }
        else
        {
            return null;
        }

        foreach (var (seasonNumber, episodeNumber) in targets)
        {
            var inStore = storedSeasons.TryGetValue(seasonNumber, out var s) && storedEpisodes.Any(e => e.SeasonId == s.Id && e.Number == episodeNumber);
            var inFetched = fetched.TryGetValue(seasonNumber, out var f) && f.Episodes.Any(e => e.Number == episodeNumber);
            if (!inStore && !inFetched)
            {
                return null;
            }
        }

        // Every provider call is done; stage the rows.
        title = StageTitle(title, details);
        foreach (var season in fetched.Values)
        {
            StageSeason(title.Id, season, storedSeasons, storedEpisodes);
        }

        var playables = new List<Playable>();
        foreach (var (seasonNumber, episodeNumber) in targets)
        {
            var seasonId = storedSeasons[seasonNumber].Id;
            var episode = storedEpisodes.Single(e => e.SeasonId == seasonId && e.Number == episodeNumber);
            playables.Add(await EpisodePlayableAsync(episode.Id, ct));
        }

        return playables;
    }

    /// <summary>The regular season and 1-based offset holding an absolute episode, counting seasons above 0 in order.</summary>
    private static (int Season, int Offset)? FindAbsolute(TitleDetails details, int absolute)
    {
        var before = 0;
        foreach (var season in details.Seasons.Where(s => s.Number > 0).OrderBy(s => s.Number))
        {
            if (absolute >= 1 && absolute <= before + season.EpisodeCount)
            {
                return (season.Number, absolute - before);
            }

            before += season.EpisodeCount;
        }

        return null;
    }

    private Task<Title?> FindTitleAsync(ExternalId id, CancellationToken ct) => db.Titles.WithExternalId(id).FirstOrDefaultAsync(ct);

    /// <summary>Returns the stored title, or adds a new one with its artwork from <paramref name="details"/>.</summary>
    private Title StageTitle(Title? stored, TitleDetails? details)
    {
        if (stored is not null)
        {
            return stored;
        }

        ArgumentNullException.ThrowIfNull(details);
        var title = new Title
        {
            Id = Ids.New(clock),
            Kind = details.Kind,
            Name = details.Name,
            Year = details.Year,
            Overview = details.Overview,
            Genres = [.. details.Genres],
            Rating = details.Rating,
            RuntimeMinutes = details.RuntimeMinutes,
        };
        foreach (var id in details.ExternalIds.Prepend(details.Id))
        {
            SetExternalId(title.ExternalIds, id);
        }

        db.Titles.Add(title);
        foreach (var art in details.Artwork.GroupBy(a => a.Type).Select(PreferredArtwork))
        {
            db.Artwork.Add(new Artwork { Id = Ids.New(clock), TitleId = title.Id, Type = art.Type, Url = art.Url });
        }

        return title;
    }

    /// <summary>Adds the season if it's new, plus its episodes the store doesn't have, each with its still.</summary>
    private void StageSeason(Guid titleId, SeasonDetails details, Dictionary<int, Season> storedSeasons, List<Episode> storedEpisodes)
    {
        if (!storedSeasons.TryGetValue(details.Number, out var season))
        {
            season = new Season { Id = Ids.New(clock), TitleId = titleId, Number = details.Number, Name = details.Name, Overview = details.Overview };
            db.Seasons.Add(season);
            storedSeasons[season.Number] = season;
            if (details.PosterUrl is { } poster)
            {
                db.Artwork.Add(new Artwork { Id = Ids.New(clock), SeasonId = season.Id, Type = ArtworkType.Poster, Url = poster });
            }
        }

        foreach (var e in details.Episodes)
        {
            if (storedEpisodes.Any(x => x.SeasonId == season.Id && x.Number == e.Number))
            {
                continue;
            }

            var episode = new Episode
            {
                Id = Ids.New(clock),
                SeasonId = season.Id,
                Number = e.Number,
                AbsoluteNumber = e.AbsoluteNumber,
                Name = e.Name,
                Overview = e.Overview,
                AirDate = e.AirDate,
            };
            db.Episodes.Add(episode);
            storedEpisodes.Add(episode);
            if (e.StillUrl is { } still)
            {
                db.Artwork.Add(new Artwork { Id = Ids.New(clock), EpisodeId = episode.Id, Type = ArtworkType.Still, Url = still });
            }
        }
    }

    private async Task<Playable> MoviePlayableAsync(Guid titleId, CancellationToken ct)
    {
        var playable = db.Playables.Local.FirstOrDefault(p => p.TitleId == titleId)
            ?? await db.Playables.FirstOrDefaultAsync(p => p.TitleId == titleId, ct);
        if (playable is null)
        {
            playable = Playable.ForMovie(Ids.New(clock), titleId);
            db.Playables.Add(playable);
        }

        return playable;
    }

    private async Task<Playable> EpisodePlayableAsync(Guid episodeId, CancellationToken ct)
    {
        var playable = db.Playables.Local.FirstOrDefault(p => p.EpisodeId == episodeId)
            ?? await db.Playables.FirstOrDefaultAsync(p => p.EpisodeId == episodeId, ct);
        if (playable is null)
        {
            playable = Playable.ForEpisode(Ids.New(clock), episodeId);
            db.Playables.Add(playable);
        }

        return playable;
    }

    /// <summary>English or language-neutral art first, then the provider's order.</summary>
    private static ArtworkRef PreferredArtwork(IEnumerable<ArtworkRef> refs) =>
        refs.OrderBy(a => a.Language is null || a.Language.Equals("en", StringComparison.OrdinalIgnoreCase) ? 0 : 1).First();

    private static void SetExternalId(TitleExternalIds ids, ExternalId id)
    {
        switch (id.Provider)
        {
            case ExternalProviders.Tmdb:
                ids.Tmdb ??= id.Value;
                break;
            case ExternalProviders.Imdb:
                ids.Imdb ??= id.Value;
                break;
            case ExternalProviders.Tvdb:
                ids.Tvdb ??= id.Value;
                break;
            case ExternalProviders.AniList:
                ids.AniList ??= id.Value;
                break;
        }
    }
}
