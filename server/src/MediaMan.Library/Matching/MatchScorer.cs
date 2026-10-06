using MediaMan.Core.Domain;
using MediaMan.Core.Modules;

namespace MediaMan.Library.Matching;

/// <summary>
/// Scores a provider candidate against a parsed release from 0 to 1 on title similarity, year, kind and, for shows,
/// whether the episode exists (SPEC §7.2 step 4). Pure: same input, same score.
/// </summary>
public static class MatchScorer
{
    /// <summary>Show when the release names a season, episode or absolute episode; otherwise movie.</summary>
    public static TitleKind ExpectedKind(ParsedRelease release)
    {
        ArgumentNullException.ThrowIfNull(release);
        return release.Season is not null || release.Episodes.Count > 0 || release.AbsoluteEpisode is not null
            ? TitleKind.Show
            : TitleKind.Movie;
    }

    /// <summary>
    /// The candidate's score. For shows, <paramref name="details"/> supplies the season list used for episode
    /// existence; without it that part scores 0, so an unverified episode can't reach the threshold on title alone.
    /// </summary>
    public static double Score(ParsedRelease release, TitleCandidate candidate, TitleDetails? details)
    {
        ArgumentNullException.ThrowIfNull(release);
        ArgumentNullException.ThrowIfNull(candidate);
        var expected = ExpectedKind(release);
        var title = release.Title is null ? 0 : TitleSimilarity.Score(release.Title, candidate.Name);
        var year = YearScore(release.Year, candidate.Year);
        var kind = candidate.Kind == expected ? 1.0 : 0.0;

        if (expected == TitleKind.Movie)
        {
            return (0.7 * title) + (0.2 * year) + (0.1 * kind);
        }

        var episode = details is not null && EpisodeExists(release, details) ? 1.0 : 0.0;
        return (0.5 * title) + (0.2 * episode) + (0.2 * year) + (0.1 * kind);
    }

    /// <summary>1 for the same year, 0.8 one year off, 0.5 when either year is unknown, otherwise 0.</summary>
    public static double YearScore(int? parsed, int? candidate) => (parsed, candidate) switch
    {
        (null, _) or (_, null) => 0.5,
        var (p, c) when p == c => 1.0,
        var (p, c) when Math.Abs(p.Value - c.Value) == 1 => 0.8,
        _ => 0.0,
    };

    /// <summary>
    /// Whether the show has the release's season and episodes or, for an absolute number, at least that many
    /// regular (non-special) episodes. Episodes with no season are looked up in season 1.
    /// </summary>
    public static bool EpisodeExists(ParsedRelease release, TitleDetails details)
    {
        ArgumentNullException.ThrowIfNull(release);
        ArgumentNullException.ThrowIfNull(details);
        if (release.Season is null && release.Episodes.Count == 0 && release.AbsoluteEpisode is { } absolute)
        {
            return absolute >= 1 && absolute <= details.Seasons.Where(s => s.Number > 0).Sum(s => s.EpisodeCount);
        }

        var summary = details.Seasons.FirstOrDefault(s => s.Number == (release.Season ?? 1));
        return summary is not null && release.Episodes.All(e => e >= 1 && e <= summary.EpisodeCount);
    }
}
