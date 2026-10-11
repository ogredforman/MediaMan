using MediaMan.Core.Domain;
using MediaMan.Playback.Progress;

namespace MediaMan.Server.LibraryApi;

/// <summary>A title with a matched file, as Home ranks it; <see cref="AddedAt"/> is when its newest file was found.</summary>
public sealed record HomeTitle(
    Guid Id, string Name, int? Year, double? Rating, IReadOnlyList<string> Genres, DateTimeOffset AddedAt, bool HasBackdrop);

/// <summary>A Playable with a matched file: a movie (no slot) or an episode of a show.</summary>
public sealed record HomePlayable(Guid Id, Guid TitleId, EpisodeSlot? Slot);

/// <summary>
/// A Playable picked for a title: where to resume when resume is offered, and the recorded duration when it's known.
/// </summary>
public sealed record WatchPick(Guid TitleId, Guid PlayableId, double? ResumeSeconds = null, double? DurationSeconds = null);

/// <summary>The profile's Continue Watching and Next Up picks, most recent first, before rows are cut to size.</summary>
public sealed record WatchRows(IReadOnlyList<WatchPick> ContinueWatching, IReadOnlyList<WatchPick> NextUp);

/// <summary>What the Home screen shows, and in what order (SPEC §8 browsing, §11 Home).</summary>
public static class HomeRules
{
    public const int RowSize = 20;
    public const int GenreRowCount = 6;
    public const int MinimumGenreTitles = 3;
    public const int HeroCandidates = 10;

    /// <summary>
    /// Each title counts once, by the profile's most recent watch state on it: part-way through (resume offered) goes
    /// to Continue Watching; a finished episode puts the show's next episode with a file in Next Up, unless the profile
    /// already watched that one. Watch states on Playables without a matched file are ignored.
    /// </summary>
    public static WatchRows Watching(IReadOnlyList<HomePlayable> playables, IEnumerable<WatchState> watches)
    {
        var byId = playables.ToDictionary(p => p.Id);
        var watched = new HashSet<Guid>();
        var latest = new Dictionary<Guid, (HomePlayable Playable, WatchState State)>();
        foreach (var w in watches)
        {
            if (!byId.TryGetValue(w.PlayableId, out var playable))
            {
                continue;
            }

            if (w.Watched)
            {
                watched.Add(w.PlayableId);
            }

            if (!latest.TryGetValue(playable.TitleId, out var current)
                || w.LastWatchedAt > current.State.LastWatchedAt
                || (w.LastWatchedAt == current.State.LastWatchedAt && w.PlayableId.CompareTo(current.State.PlayableId) > 0))
            {
                latest[playable.TitleId] = (playable, w);
            }
        }

        var episodesByTitle = playables.Where(p => p.Slot is not null).ToLookup(p => p.TitleId);
        var continueWatching = new List<WatchPick>();
        var nextUp = new List<WatchPick>();
        foreach (var (titleId, (playable, state)) in latest.OrderByDescending(l => l.Value.State.LastWatchedAt).ThenBy(l => l.Key))
        {
            if (WatchRules.ResumeAt(state.PositionSeconds, state.DurationSeconds) is { } resume)
            {
                continueWatching.Add(new WatchPick(
                    titleId, playable.Id, resume, state.DurationSeconds > 0 ? state.DurationSeconds : null));
            }
            else if (state.Watched && playable.Slot is { } slot)
            {
                var episodes = episodesByTitle[titleId].GroupBy(p => p.Slot!.Value).ToDictionary(g => g.Key, g => g.First());
                if (WatchRules.NextEpisode(slot, episodes.Keys) is { } next && !watched.Contains(episodes[next].Id))
                {
                    nextUp.Add(new WatchPick(titleId, episodes[next].Id));
                }
            }
        }

        return new WatchRows(continueWatching, nextUp);
    }

    /// <summary>Titles by when their newest file was found, newest first.</summary>
    public static IReadOnlyList<HomeTitle> RecentlyAdded(IEnumerable<HomeTitle> titles) =>
        [.. NewestFirst(titles).Take(RowSize)];

    /// <summary>
    /// The genres with the most titles (at least <see cref="MinimumGenreTitles"/>, ties by name), each with its titles by
    /// rating.
    /// </summary>
    public static IReadOnlyList<(string Genre, IReadOnlyList<HomeTitle> Titles)> Genres(IEnumerable<HomeTitle> titles) =>
        [.. titles
            .SelectMany(t => t.Genres.Distinct().Select(g => (Genre: g, Title: t)))
            .GroupBy(x => x.Genre)
            .Select(g => (Genre: g.Key, Titles: g.Select(x => x.Title).ToList()))
            .Where(g => g.Titles.Count >= MinimumGenreTitles)
            .OrderByDescending(g => g.Titles.Count)
            .ThenBy(g => g.Genre, StringComparer.OrdinalIgnoreCase)
            .ThenBy(g => g.Genre, StringComparer.Ordinal)
            .Take(GenreRowCount)
            .Select(g => (g.Genre, (IReadOnlyList<HomeTitle>)[.. ByRating(g.Titles).Take(RowSize)]))];

    /// <summary>
    /// One of the <see cref="HeroCandidates"/> most recently added titles with a backdrop, a different one each UTC day;
    /// null when none has a backdrop.
    /// </summary>
    public static HomeTitle? Hero(IEnumerable<HomeTitle> titles, DateTimeOffset now)
    {
        var candidates = NewestFirst(titles.Where(t => t.HasBackdrop)).Take(HeroCandidates).ToList();
        if (candidates.Count == 0)
        {
            return null;
        }

        var day = Math.Floor(now.ToUnixTimeSeconds() / (double)TimeSpan.SecondsPerDay);
        return candidates[(int)(day % candidates.Count)];
    }

    /// <summary>
    /// What the hero's Play starts: the title's Continue Watching or Next Up pick, else the movie, else the show's first
    /// episode (regular seasons before specials). Null when none of its Playables has a file.
    /// </summary>
    public static WatchPick? HeroPlay(Guid titleId, IEnumerable<HomePlayable> playablesOfTitle, WatchRows watching) =>
        watching.ContinueWatching.FirstOrDefault(p => p.TitleId == titleId)
        ?? watching.NextUp.FirstOrDefault(p => p.TitleId == titleId)
        ?? (playablesOfTitle
                .OrderBy(p => p.Slot?.SeasonNumber == WatchRules.SpecialsSeason)
                .ThenBy(p => p.Slot?.SeasonNumber)
                .ThenBy(p => p.Slot?.EpisodeNumber)
                .ThenBy(p => p.Id)
                .FirstOrDefault() is { } first
            ? new WatchPick(titleId, first.Id)
            : null);

    private static IEnumerable<HomeTitle> NewestFirst(IEnumerable<HomeTitle> titles) =>
        titles.OrderByDescending(t => t.AddedAt).ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ThenBy(t => t.Id);

    private static IEnumerable<HomeTitle> ByRating(IEnumerable<HomeTitle> titles) =>
        titles.OrderByDescending(t => t.Rating ?? -1)
            .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(t => t.Year)
            .ThenBy(t => t.Id);
}
