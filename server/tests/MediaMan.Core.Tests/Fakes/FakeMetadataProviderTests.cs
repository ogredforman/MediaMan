using System.Net;
using MediaMan.Core.Domain;
using MediaMan.Core.Modules;
using MediaMan.Fakes;

namespace MediaMan.Core.Tests.Fakes;

public class FakeMetadataProviderTests
{
    private readonly FakeMetadataProvider _provider = new();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Fixtures_cover_the_prototype_scenarios()
    {
        _provider.Titles.Should().HaveCountGreaterThanOrEqualTo(30);
        _provider.Titles.GroupBy(t => (t.Kind, t.Name)).Should().Contain(g => g.Key.Kind == TitleKind.Movie && g.Count() > 1, "remakes share a name");
        _provider.Titles.GroupBy(t => (t.Kind, t.Name)).Should().Contain(g => g.Key.Kind == TitleKind.Show && g.Count() > 1, "same-name shows exist");
        _provider.Titles.Should().Contain(t => t.Seasons.Any(s => s.Number == 0), "a show has specials");
        _provider.Titles.Should().Contain(t => t.Seasons.Any(s => s.Episodes.Any(e => e.AbsoluteNumber != null)), "anime has absolute numbering");
    }

    [Fact]
    public async Task Search_returns_every_remake_with_the_closest_year_first()
    {
        var results = await _provider.SearchAsync(new TitleQuery("The Lion King", 2020, TitleKind.Movie), Ct);

        results.Select(r => (r.Name, r.Year)).Should().Equal(("The Lion King", 2019), ("The Lion King", 1994));
        results[0].Id.Should().Be(ExternalId.Parse("tmdb:movie/420818"));
        results[0].PosterUrl.Should().Be("https://images.invalid/tmdb/420818/poster.jpg");
    }

    [Fact]
    public async Task Search_ranks_exact_names_before_longer_ones_and_ignores_case_accents_and_punctuation()
    {
        var blade = await _provider.SearchAsync(new TitleQuery("blade.runner", null, null), Ct);
        var amelie = await _provider.SearchAsync(new TitleQuery("AMELIE", 2001, null), Ct);

        blade.Select(r => r.Name).Should().Equal("Blade Runner", "Blade Runner 2049");
        amelie.Should().ContainSingle().Which.Name.Should().Be("Amélie");
    }

    [Fact]
    public async Task Search_filters_by_kind_but_not_by_year()
    {
        var shows = await _provider.SearchAsync(new TitleQuery("The Office", 1990, TitleKind.Show), Ct);
        var movies = await _provider.SearchAsync(new TitleQuery("The Office", null, TitleKind.Movie), Ct);

        shows.Select(r => r.Year).Should().Equal(2001, 2005);
        movies.Should().BeEmpty();
    }

    [Fact]
    public async Task Title_and_season_details_include_specials_and_absolute_numbers()
    {
        var doctorWho = await _provider.GetTitleAsync(ExternalId.Parse("tmdb:tv/57243"), Ct);
        var specials = await _provider.GetSeasonAsync(doctorWho.Id, 0, Ct);
        var onePiece = await _provider.GetSeasonAsync(ExternalId.Parse("tmdb:tv/37854"), 2, Ct);

        doctorWho.Kind.Should().Be(TitleKind.Show);
        doctorWho.Seasons.Select(s => s.Number).Should().Equal(0, 1, 2);
        doctorWho.ExternalIds.Should().Contain(ExternalId.Parse("tvdb:78804"));
        specials.Episodes[0].Should().Be(new EpisodeDetails(1, null, "The Christmas Invasion", null,
            new DateOnly(2005, 12, 25), "https://images.invalid/tmdb/57243/s0e1.jpg"));
        onePiece.Episodes.Select(e => e.AbsoluteNumber).Should().Equal(9, 10, 11, 12, 13, 14);
    }

    [Fact]
    public async Task Unknown_ids_and_seasons_throw_not_found()
    {
        var unknownTitle = () => _provider.GetTitleAsync(ExternalId.Parse("tmdb:movie/999999999"), Ct);
        var unknownSeason = () => _provider.GetSeasonAsync(ExternalId.Parse("tmdb:tv/1396"), 99, Ct);

        await unknownTitle.Should().ThrowAsync<KeyNotFoundException>();
        await unknownSeason.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Queued_errors_fail_the_next_calls_then_clear()
    {
        _provider.FailNext(FakeProviderError.Timeout);
        _provider.FailNext(FakeProviderError.RateLimited);
        var query = new TitleQuery("Dune", null, null);

        await FluentActions.Awaiting(() => _provider.SearchAsync(query, Ct)).Should().ThrowAsync<TimeoutException>();
        (await FluentActions.Awaiting(() => _provider.GetTitleAsync(ExternalId.Parse("tmdb:movie/841"), Ct))
            .Should().ThrowAsync<HttpRequestException>()).Which.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await _provider.SearchAsync(query, Ct)).Should().HaveCount(2);
    }

    [Fact]
    public async Task Season_errors_hit_only_season_calls()
    {
        _provider.FailNextSeason(FakeProviderError.Timeout);
        var show = ExternalId.Parse("tmdb:tv/19885");

        await _provider.GetTitleAsync(show, Ct);
        await FluentActions.Awaiting(() => _provider.GetSeasonAsync(show, 1, Ct)).Should().ThrowAsync<TimeoutException>();
        (await _provider.GetSeasonAsync(show, 1, Ct)).Number.Should().Be(1);
    }

    [Fact]
    public async Task Every_call_is_recorded_including_failed_ones()
    {
        _provider.FailNext(FakeProviderError.Timeout);
        var query = new TitleQuery("Sherlock", 2010, TitleKind.Show);

        await FluentActions.Awaiting(() => _provider.SearchAsync(query, Ct)).Should().ThrowAsync<TimeoutException>();
        await _provider.GetTitleAsync(ExternalId.Parse("tmdb:tv/19885"), Ct);
        await _provider.GetSeasonAsync(ExternalId.Parse("tmdb:tv/19885"), 1, Ct);

        _provider.SearchQueries.Should().Equal(query);
        _provider.TitleRequests.Should().Equal(ExternalId.Parse("tmdb:tv/19885"));
        _provider.SeasonRequests.Should().Equal((ExternalId.Parse("tmdb:tv/19885"), 1));
    }

    [Fact]
    public void A_provider_only_serves_fixtures_with_its_own_key()
    {
        new FakeMetadataProvider(key: ExternalProviders.AniList).Titles.Should().BeEmpty();
    }
}
