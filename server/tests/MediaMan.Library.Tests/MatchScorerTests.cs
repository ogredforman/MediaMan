using MediaMan.Core;
using MediaMan.Core.Domain;
using MediaMan.Core.Modules;
using MediaMan.Library.Matching;

namespace MediaMan.Library.Tests;

public sealed class MatchScorerTests
{
    private static readonly ExternalId _showId = new(ExternalProviders.Tmdb, "tv/1");

    private static readonly TitleDetails _show = new()
    {
        Id = _showId,
        Kind = TitleKind.Show,
        Name = "Example Show",
        Seasons = [new SeasonSummary(0, "Specials", 2), new SeasonSummary(1, null, 6), new SeasonSummary(2, null, 5)],
    };

    [Theory]
    [InlineData("Amélie", "Amelie", 1.0)]
    [InlineData("Marvel's Agents of S.H.I.E.L.D.", "Marvels Agents of S H I E L D", 1.0)]
    [InlineData("Law & Order", "Law and Order", 1.0)]
    [InlineData("Alien", "Aliens", 5.0 / 6)]
    [InlineData("", "Alien", 0.0)]
    public void Title_similarity_ignores_case_accents_and_punctuation(string a, string b, double expected) =>
        TitleSimilarity.Score(a, b).Should().BeApproximately(expected, 1e-9);

    [Theory]
    [InlineData(2010, 2010, 1.0)]
    [InlineData(2010, 2011, 0.8)]
    [InlineData(2011, 2010, 0.8)]
    [InlineData(2010, 2012, 0.0)]
    [InlineData(null, 2010, 0.5)]
    [InlineData(2010, null, 0.5)]
    public void Year_score_allows_one_year_off(int? parsed, int? candidate, double expected) =>
        MatchScorer.YearScore(parsed, candidate).Should().Be(expected);

    [Fact]
    public void Expected_kind_is_show_when_any_episode_numbering_is_present()
    {
        MatchScorer.ExpectedKind(new ParsedRelease { Title = "X" }).Should().Be(TitleKind.Movie);
        MatchScorer.ExpectedKind(new ParsedRelease { Title = "X", Season = 1 }).Should().Be(TitleKind.Show);
        MatchScorer.ExpectedKind(new ParsedRelease { Title = "X", Episodes = [3] }).Should().Be(TitleKind.Show);
        MatchScorer.ExpectedKind(new ParsedRelease { Title = "X", AbsoluteEpisode = 100 }).Should().Be(TitleKind.Show);
    }

    [Theory]
    [InlineData(1, new[] { 6 }, null, true)]
    [InlineData(1, new[] { 7 }, null, false)]
    [InlineData(2, new[] { 4, 5 }, null, true)]
    [InlineData(2, new[] { 5, 6 }, null, false)]
    [InlineData(0, new[] { 2 }, null, true)]
    [InlineData(3, new[] { 1 }, null, false)]
    [InlineData(null, new[] { 6 }, null, true)]
    [InlineData(null, new int[0], 11, true)]
    [InlineData(null, new int[0], 12, false)]
    public void Episode_exists_checks_seasons_specials_and_absolute_numbers(int? season, int[] episodes, int? absolute, bool expected)
    {
        var release = new ParsedRelease { Title = "Example Show", Season = season, Episodes = episodes, AbsoluteEpisode = absolute };

        MatchScorer.EpisodeExists(release, _show).Should().Be(expected);
    }

    [Fact]
    public void Movie_score_weights_title_year_and_kind()
    {
        var release = new ParsedRelease { Title = "Dune", Year = 2021 };

        MatchScorer.Score(release, Candidate("Dune", 2021, TitleKind.Movie), null).Should().BeApproximately(1.0, 1e-9);
        MatchScorer.Score(release, Candidate("Dune", 1984, TitleKind.Movie), null).Should().BeApproximately(0.8, 1e-9);
        MatchScorer.Score(release, Candidate("Dune", 2021, TitleKind.Show), null).Should().BeApproximately(0.9, 1e-9);
    }

    [Fact]
    public void Show_score_needs_the_episode_to_exist_to_pass_the_threshold()
    {
        var candidate = Candidate("Example Show", null, TitleKind.Show);

        MatchScorer.Score(new ParsedRelease { Title = "Example Show", Season = 1, Episodes = [2] }, candidate, _show)
            .Should().BeApproximately(0.9, 1e-9);
        MatchScorer.Score(new ParsedRelease { Title = "Example Show", Season = 4, Episodes = [2] }, candidate, _show)
            .Should().BeApproximately(0.7, 1e-9);
        MatchScorer.Score(new ParsedRelease { Title = "Example Show", Season = 1, Episodes = [2] }, candidate, null)
            .Should().BeApproximately(0.7, 1e-9);
    }

    private static TitleCandidate Candidate(string name, int? year, TitleKind kind) =>
        new(_showId, kind, name, year, null, null);
}
