using System.Net;
using System.Net.Http.Headers;
using MediaMan.Core;
using MediaMan.Core.Domain;
using MediaMan.Core.Modules;
using MediaMan.Fakes;
using MediaMan.Metadata.Tmdb;

namespace MediaMan.Metadata.Tests.Tmdb;

public sealed class TmdbMetadataProviderTests : IDisposable
{
    private const string MatrixSearch = "search/movie?query=The%20Matrix&include_adult=false&language=en-US&page=1";
    private const string MatrixSearch1999 = MatrixSearch + "&year=1999";
    private const string BreakingBadSearch = "search/tv?query=Breaking%20Bad&include_adult=false&language=en-US&page=1";
    private const string Matrix = "movie/603?append_to_response=external_ids,images&include_image_language=en,null&language=en-US";
    private const string BreakingBad = "tv/1396?append_to_response=external_ids,images&include_image_language=en,null&language=en-US";
    private const string BreakingBadSeason1 = "tv/1396/season/1?language=en-US";
    private const string Image = "https://image.tmdb.org/t/p/original";

    private readonly string _cacheDirectory = Path.Combine(Path.GetTempPath(), "mediaman-tests", Guid.NewGuid().ToString("N"));
    private readonly FakeClock _clock = new();
    private readonly List<TimeSpan> _waits = [];
    private readonly StubTmdbHandler _handler = new StubTmdbHandler()
        .Record(MatrixSearch, "search-movie-matrix")
        .Record(MatrixSearch1999, "search-movie-matrix-1999")
        .Record(BreakingBadSearch, "search-tv-breaking-bad")
        .Record(Matrix, "movie-603")
        .Record(BreakingBad, "tv-1396")
        .Record(BreakingBadSeason1, "tv-1396-season-1");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        if (Directory.Exists(_cacheDirectory))
        {
            Directory.Delete(_cacheDirectory, recursive: true);
        }
    }

    private TmdbMetadataProvider Provider(string? accessToken = null) => new(
        new HttpClient(_handler, disposeHandler: false),
        new TmdbOptions { AccessToken = accessToken, CacheDirectory = _cacheDirectory },
        _clock,
        (wait, _) =>
        {
            _waits.Add(wait);
            return Task.CompletedTask;
        });

    [Fact]
    public async Task Search_puts_year_matches_first_then_the_rest_without_duplicates()
    {
        using var provider = Provider();

        var results = await provider.SearchAsync(new TitleQuery("The Matrix", 1999, TitleKind.Movie), Ct);

        results[0].Should().Be(new TitleCandidate(
            ExternalId.Parse("tmdb:movie/603"),
            TitleKind.Movie,
            "The Matrix",
            1999,
            results[0].Overview,
            $"{Image}/dXNAPwY7VrqMAo51EKhhCJfaGb5.jpg"));
        results[0].Overview.Should().StartWith("Set in the 22nd century");
        results.Select(r => r.Id.Value).Should().StartWith(["movie/603", "movie/684431", "movie/684428", "movie/604"])
            .And.OnlyHaveUniqueItems();
        results.Should().Contain(r => r.Name == "The Matrix Resurrections" && r.Year == 2021);
        results.Single(r => r.Id.Value == "movie/684428").PosterUrl.Should().BeNull();
    }

    [Fact]
    public async Task Search_for_a_show_uses_the_tv_endpoint()
    {
        using var provider = Provider();

        var results = await provider.SearchAsync(new TitleQuery("Breaking Bad", null, TitleKind.Show), Ct);

        results[0].Id.Should().Be(ExternalId.Parse("tmdb:tv/1396"));
        results[0].Kind.Should().Be(TitleKind.Show);
        results[0].Year.Should().Be(2008);
        _handler.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task Search_without_a_kind_alternates_movies_and_shows()
    {
        _handler
            .Record("search/movie?query=Breaking%20Bad&include_adult=false&language=en-US&page=1", "search-movie-matrix")
            .Record("search/tv?query=The%20Matrix&include_adult=false&language=en-US&page=1", "search-tv-breaking-bad");
        using var provider = Provider();

        var results = await provider.SearchAsync(new TitleQuery("Breaking Bad", null, null), Ct);

        results.Take(4).Select(r => r.Kind).Should().Equal(TitleKind.Movie, TitleKind.Show, TitleKind.Movie, TitleKind.Show);
    }

    [Fact]
    public async Task A_blank_search_makes_no_request()
    {
        using var provider = Provider();

        (await provider.SearchAsync(new TitleQuery("  ", 1999, null), Ct)).Should().BeEmpty();

        _handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Movie_details_map_ids_rating_runtime_and_artwork()
    {
        using var provider = Provider();

        var matrix = await provider.GetTitleAsync(ExternalId.Parse("tmdb:movie/603"), Ct);

        matrix.Id.Should().Be(ExternalId.Parse("tmdb:movie/603"));
        matrix.Kind.Should().Be(TitleKind.Movie);
        matrix.Name.Should().Be("The Matrix");
        matrix.Year.Should().Be(1999);
        matrix.Genres.Should().Equal("Action", "Science Fiction");
        matrix.Rating.Should().BeInRange(7.0, 9.5).And.Be(Math.Round(matrix.Rating!.Value, 1));
        matrix.RuntimeMinutes.Should().Be(136);
        matrix.ExternalIds.Should().Equal(ExternalId.Parse("imdb:tt0133093"));
        matrix.Seasons.Should().BeEmpty();
        matrix.Artwork.Should().Equal(
            new ArtworkRef(ArtworkType.Poster, $"{Image}/dXNAPwY7VrqMAo51EKhhCJfaGb5.jpg", "en"),
            new ArtworkRef(ArtworkType.Backdrop, matrix.Artwork[1].Url, null),
            new ArtworkRef(ArtworkType.Logo, $"{Image}/xslT5YyQOaqcVKAZxcoq2YbcbEf.svg", "en"));
        matrix.Artwork[1].Url.Should().StartWith(Image + "/");
    }

    [Fact]
    public async Task Show_details_map_seasons_and_the_tvdb_id()
    {
        using var provider = Provider();

        var show = await provider.GetTitleAsync(ExternalId.Parse("tmdb:tv/1396"), Ct);

        show.Kind.Should().Be(TitleKind.Show);
        show.Name.Should().Be("Breaking Bad");
        show.Year.Should().Be(2008);
        show.RuntimeMinutes.Should().BeNull("TMDB sends no episode run time for this show");
        show.ExternalIds.Should().Equal(ExternalId.Parse("imdb:tt0903747"), ExternalId.Parse("tvdb:81189"));
        show.Seasons.Should().HaveCount(6);
        show.Seasons[0].Should().Be(new SeasonSummary(0, "Specials", 9));
        show.Seasons[1].Should().Be(new SeasonSummary(1, "Season 1", 7));
    }

    [Fact]
    public async Task Season_details_map_episodes_with_air_dates_and_stills()
    {
        using var provider = Provider();

        var season = await provider.GetSeasonAsync(ExternalId.Parse("tmdb:tv/1396"), 1, Ct);

        season.ShowId.Should().Be(ExternalId.Parse("tmdb:tv/1396"));
        season.Number.Should().Be(1);
        season.Episodes.Should().HaveCount(7);
        season.Episodes[0].Should().Be(new EpisodeDetails(
            1,
            null,
            "Pilot",
            season.Episodes[0].Overview,
            new DateOnly(2008, 1, 20),
            $"{Image}/ydlY3iPfeOAvu8gVqrxPoMvzNCn.jpg"));
        season.Episodes[0].Overview.Should().StartWith("When an unassuming high school chemistry teacher");
    }

    [Fact]
    public async Task Ids_must_be_tmdb_ids_with_a_kind()
    {
        using var provider = Provider();

        await FluentActions.Awaiting(() => provider.GetTitleAsync(ExternalId.Parse("tmdb:603"), Ct))
            .Should().ThrowAsync<ArgumentException>().WithMessage("*tmdb:movie/603*");
        await FluentActions.Awaiting(() => provider.GetTitleAsync(ExternalId.Parse("imdb:tt0133093"), Ct))
            .Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => provider.GetSeasonAsync(ExternalId.Parse("tmdb:movie/603"), 1, Ct))
            .Should().ThrowAsync<ArgumentException>().WithMessage("*movie*");
        _handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task An_unknown_title_throws_KeyNotFoundException()
    {
        _handler.Enqueue(
            "movie/1?append_to_response=external_ids,images&include_image_language=en,null&language=en-US",
            () => StubTmdbHandler.Json(HttpStatusCode.NotFound, File.ReadAllText(Path.Combine(StubTmdbHandler.FixtureDirectory, "movie-0.json"))));
        using var provider = Provider();

        await FluentActions.Awaiting(() => provider.GetTitleAsync(ExternalId.Parse("tmdb:movie/1"), Ct))
            .Should().ThrowAsync<KeyNotFoundException>().WithMessage("*tmdb:movie/1*");
    }

    [Fact]
    public async Task Responses_are_cached_for_seven_days_and_survive_a_restart()
    {
        using (var provider = Provider())
        {
            await provider.GetTitleAsync(ExternalId.Parse("tmdb:movie/603"), Ct);
            await provider.GetTitleAsync(ExternalId.Parse("tmdb:movie/603"), Ct);
        }

        _clock.Advance(TimeSpan.FromDays(7) - TimeSpan.FromSeconds(1));
        using (var restarted = Provider())
        {
            (await restarted.GetTitleAsync(ExternalId.Parse("tmdb:movie/603"), Ct)).Name.Should().Be("The Matrix");
            _handler.CountOf(Matrix).Should().Be(1);

            _clock.Advance(TimeSpan.FromSeconds(1));
            await restarted.GetTitleAsync(ExternalId.Parse("tmdb:movie/603"), Ct);
            _handler.CountOf(Matrix).Should().Be(2);
        }
    }

    [Fact]
    public async Task Failed_responses_are_not_cached()
    {
        _handler.Enqueue(Matrix, () => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        using var provider = Provider();

        await FluentActions.Awaiting(() => provider.GetTitleAsync(ExternalId.Parse("tmdb:movie/603"), Ct))
            .Should().ThrowAsync<HttpRequestException>().Where(e => e.StatusCode == HttpStatusCode.ServiceUnavailable);
        (await provider.GetTitleAsync(ExternalId.Parse("tmdb:movie/603"), Ct)).Name.Should().Be("The Matrix");
    }

    [Fact]
    public async Task An_unreadable_response_is_a_retryable_error_and_is_not_cached()
    {
        _handler.Enqueue(Matrix, () => StubTmdbHandler.Json(HttpStatusCode.OK, "<html>maintenance</html>"));
        using var provider = Provider();

        await FluentActions.Awaiting(() => provider.GetTitleAsync(ExternalId.Parse("tmdb:movie/603"), Ct))
            .Should().ThrowAsync<HttpRequestException>().WithMessage("*unreadable*");
        (await provider.GetTitleAsync(ExternalId.Parse("tmdb:movie/603"), Ct)).Name.Should().Be("The Matrix");
        _handler.CountOf(Matrix).Should().Be(2);
    }

    [Fact]
    public async Task A_damaged_cache_entry_is_refetched()
    {
        using var provider = Provider();
        await provider.GetTitleAsync(ExternalId.Parse("tmdb:movie/603"), Ct);
        foreach (var file in Directory.EnumerateFiles(_cacheDirectory))
        {
            await File.WriteAllTextAsync(file, "{ not json", Ct);
        }

        (await provider.GetTitleAsync(ExternalId.Parse("tmdb:movie/603"), Ct)).Name.Should().Be("The Matrix");
        _handler.CountOf(Matrix).Should().Be(2);
    }

    [Fact]
    public async Task Rate_limiting_waits_for_retry_after_then_retries()
    {
        _handler
            .Enqueue(Matrix, () => RateLimited(TimeSpan.FromSeconds(3)))
            .Enqueue(Matrix, () => RateLimited(null))
            .Enqueue(Matrix, () => RateLimited(TimeSpan.FromMinutes(10)));
        using var provider = Provider();

        (await provider.GetTitleAsync(ExternalId.Parse("tmdb:movie/603"), Ct)).Name.Should().Be("The Matrix");

        _waits.Should().Equal(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(30));
        _handler.CountOf(Matrix).Should().Be(4);
    }

    [Fact]
    public async Task Rate_limiting_that_outlasts_the_retries_throws_429_for_the_caller_to_retry()
    {
        for (var i = 0; i < 4; i++)
        {
            _handler.Enqueue(Matrix, () => RateLimited(null));
        }

        using var provider = Provider();

        await FluentActions.Awaiting(() => provider.GetTitleAsync(ExternalId.Parse("tmdb:movie/603"), Ct))
            .Should().ThrowAsync<HttpRequestException>().Where(e => e.StatusCode == HttpStatusCode.TooManyRequests);
        _waits.Should().Equal(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4));
    }

    [Fact]
    public async Task A_rejected_token_says_how_to_fix_it()
    {
        _handler.Enqueue(Matrix, () => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        using var provider = Provider();

        await FluentActions.Awaiting(() => provider.GetTitleAsync(ExternalId.Parse("tmdb:movie/603"), Ct))
            .Should().ThrowAsync<HttpRequestException>().WithMessage("*access token*MediaMan:Metadata:Tmdb:AccessToken*");
    }

    [Fact]
    public async Task The_token_goes_only_in_the_authorization_header()
    {
        using (var provider = Provider())
        {
            await provider.SearchAsync(new TitleQuery("The Matrix", 1999, TitleKind.Movie), Ct);
            await provider.GetTitleAsync(ExternalId.Parse("tmdb:movie/603"), Ct);
        }

        using (var overridden = Provider(accessToken: " custom-token "))
        {
            await overridden.GetSeasonAsync(ExternalId.Parse("tmdb:tv/1396"), 1, Ct);
        }

        _handler.Requests.Select(r => r.Headers.Authorization).Should().Equal(
            new AuthenticationHeaderValue("Bearer", TmdbOptions.ShippedAccessToken),
            new AuthenticationHeaderValue("Bearer", TmdbOptions.ShippedAccessToken),
            new AuthenticationHeaderValue("Bearer", TmdbOptions.ShippedAccessToken),
            new AuthenticationHeaderValue("Bearer", "custom-token"));
        _handler.Requests.Should().AllSatisfy(r => r.RequestUri!.ToString().Should().NotContain(TmdbOptions.ShippedAccessToken[^20..]));
        foreach (var file in Directory.EnumerateFiles(_cacheDirectory))
        {
            Path.GetFileName(file).Should().MatchRegex("^[0-9a-f]{64}\\.json$");
            (await File.ReadAllTextAsync(file, Ct)).Should().NotContain(TmdbOptions.ShippedAccessToken[^20..])
                .And.NotContain("custom-token");
        }
    }

    [Fact]
    public async Task Cancellation_is_passed_through()
    {
        using var provider = Provider();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await FluentActions.Awaiting(() => provider.GetTitleAsync(ExternalId.Parse("tmdb:movie/603"), cts.Token))
            .Should().ThrowAsync<OperationCanceledException>();
    }

    [Theory]
    [InlineData("tmdb:movie/603", TitleKind.Movie, 603)]
    [InlineData("tmdb:tv/1396", TitleKind.Show, 1396)]
    public void Ids_round_trip(string text, TitleKind kind, int number)
    {
        TmdbIds.Parse(ExternalId.Parse(text)).Should().Be((kind, number));
        TmdbIds.Create(kind, number).ToString().Should().Be(text);
    }

    [Theory]
    [InlineData("tmdb:603")]
    [InlineData("tmdb:movie/")]
    [InlineData("tmdb:movie/-1")]
    [InlineData("tmdb:person/31")]
    [InlineData("anilist:movie/21")]
    public void Malformed_ids_are_rejected(string text)
    {
        TmdbIds.TryParse(ExternalId.Parse(text), out _, out _).Should().BeFalse();
    }

    private static HttpResponseMessage RateLimited(TimeSpan? retryAfter)
    {
        var response = StubTmdbHandler.Json(HttpStatusCode.TooManyRequests, """{"status_code":25}""");
        if (retryAfter is { } wait)
        {
            response.Headers.RetryAfter = new RetryConditionHeaderValue(wait);
        }

        return response;
    }
}
