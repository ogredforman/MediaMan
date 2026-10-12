using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MediaMan.Core;
using MediaMan.Data;
using MediaMan.Fakes;
using MediaMan.Server.Endpoints;
using MediaMan.Server.LibraryApi;
using MediaMan.Server.Tests.Conformance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MediaMan.Server.Tests;

/// <summary>
/// <c>PUT /playables/{playableId}/watched</c> and <c>PUT /titles/{titleId}/watched</c>: mark watched and unwatched
/// (SPEC §8 browsing), and the <c>watched</c> marker on title cards in <c>/titles</c>, <c>/search</c> and <c>/home</c>.
/// </summary>
public sealed class WatchedApiTests : IAsyncLifetime
{
    private readonly FakeClock _clock = new();
    private readonly MediaManFactory _factory;
    private readonly string _media = Path.Combine(Path.GetTempPath(), "mediaman-tests", Guid.NewGuid().ToString("N"));
    private HttpClient _client = null!;
    private Guid _profileId;
    private WatchLibrary _library = null!;

    public WatchedApiTests() =>
        _factory = new MediaManFactory { ConfigureServices = services => services.AddSingleton<IClock>(_clock) };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Guid Episode(string key) => _library.Episodes[key];

    public async ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(_media);
        var user = await _factory.CreateUserAsync(Ct, [.. Permissions.All]);
        _client = _factory.CreateClient(user);
        var profiles = await _client.GetFromJsonAsync<ProfileList>("/profiles", JsonSerializerOptions.Web, Ct);
        _profileId = profiles!.Items[0].Id;
        using (var picked = await _client.PutAsJsonAsync("/auth/session/profile", new SelectProfileRequest(_profileId), Ct))
        {
            picked.IsSuccessStatusCode.Should().BeTrue(await picked.Content.ReadAsStringAsync(Ct));
        }

        await using var scope = _factory.Services.CreateAsyncScope();
        _library = await WatchLibrary.SeedAsync(scope.ServiceProvider.GetRequiredService<MediaManDbContext>(), _media, Ct);
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        Directory.Delete(_media, recursive: true);
    }

    [Fact]
    public async Task Marking_a_movie_watched_drops_its_resume_point_and_unwatched_starts_it_fresh()
    {
        await ReportAsync(_library.Movie, 1500, 6000);

        await MarkPlayableAsync(_library.Movie, true);

        var movie = (await TitleAsync(_library.MovieTitle)).Playable!;
        (movie.ResumeSeconds, movie.Watched).Should().Be(((double?)null, true));
        (await SummaryAsync(_library.MovieTitle)).Watched.Should().BeTrue();

        await MarkPlayableAsync(_library.Movie, false);

        movie = (await TitleAsync(_library.MovieTitle)).Playable!;
        (movie.ResumeSeconds, movie.Watched).Should().Be(((double?)null, false));
        (await SummaryAsync(_library.MovieTitle)).Watched.Should().BeFalse();
        (await WatchStateCountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Marking_an_episode_marks_only_that_episode()
    {
        await MarkPlayableAsync(Episode("1x2"), true);

        var watched = await EpisodesWatchedAsync();
        watched.Where(e => e.Value).Select(e => e.Key).Should().Equal(Episode("1x2"));
        (await SummaryAsync(_library.ShowTitle)).Watched.Should().BeFalse();
    }

    [Fact]
    public async Task A_show_is_watched_once_every_season_outside_specials_is()
    {
        await MarkTitleAsync(_library.ShowTitle, true, seasonNumber: 1);

        var watched = await EpisodesWatchedAsync();
        watched[Episode("1x1")].Should().BeTrue();
        watched[Episode("1x2")].Should().BeTrue();
        watched[Episode("2x1")].Should().BeFalse();
        watched[Episode("0x1")].Should().BeFalse();
        (await SummaryAsync(_library.ShowTitle)).Watched.Should().BeFalse();

        await MarkTitleAsync(_library.ShowTitle, true, seasonNumber: 2);

        (await SummaryAsync(_library.ShowTitle)).Watched.Should().BeTrue("specials don't count");
        var search = await GetAsync<LibrarySearchResults>("/search?q=office");
        search.Titles.Single(t => t.Id == _library.ShowTitle).Watched.Should().BeTrue();
        var home = await GetAsync<HomeDto>("/home");
        home.Rows.SelectMany(r => r.Items).Where(i => i.Title.Id == _library.ShowTitle).Should().NotBeEmpty()
            .And.OnlyContain(i => i.Title.Watched);
    }

    [Fact]
    public async Task After_a_season_is_marked_watched_next_up_offers_the_next_season()
    {
        await MarkTitleAsync(_library.ShowTitle, true, seasonNumber: 1);

        var home = await GetAsync<HomeDto>("/home");
        var nextUp = home.Rows.Single(r => r.Kind == HomeRow.NextUp).Items;
        nextUp.Should().ContainSingle().Which.PlayableId.Should().Be(Episode("2x1"));
    }

    [Fact]
    public async Task Marking_a_whole_show_unwatched_clears_every_episode()
    {
        await ReportAsync(Episode("2x1"), 600, 1300);
        await MarkTitleAsync(_library.ShowTitle, true);
        (await EpisodesWatchedAsync()).Values.Should().AllSatisfy(w => w.Should().BeTrue());

        await MarkTitleAsync(_library.ShowTitle, false);

        (await EpisodesWatchedAsync()).Values.Should().AllSatisfy(w => w.Should().BeFalse());
        (await WatchStateCountAsync()).Should().Be(0);
        var home = await GetAsync<HomeDto>("/home");
        home.Rows.Where(r => r.Kind is HomeRow.ContinueWatching or HomeRow.NextUp).SelectMany(r => r.Items).Should().BeEmpty();
    }

    [Fact]
    public async Task Marking_a_movie_title_marks_its_playable()
    {
        await MarkTitleAsync(_library.MovieTitle, true);

        (await TitleAsync(_library.MovieTitle)).Playable!.Watched.Should().BeTrue();
    }

    [Fact]
    public async Task Another_profile_and_a_session_without_one_see_nothing_watched()
    {
        await MarkTitleAsync(_library.MovieTitle, true);

        using var noProfile = await _factory.CreateSignedInClientAsync(Ct);
        var page = await noProfile.GetFromJsonAsync<TitlePage>("/titles", JsonSerializerOptions.Web, Ct);
        page!.Items.Should().OnlyContain(t => !t.Watched);

        using (var created = await _client.PostAsJsonAsync("/profiles", new CreateProfileRequest("Kid", null), Ct))
        {
            var other = (await created.Content.ReadFromJsonAsync<ProfileDto>(JsonSerializerOptions.Web, Ct))!.Id;
            using var picked = await _client.PutAsJsonAsync("/auth/session/profile", new SelectProfileRequest(other), Ct);
            picked.IsSuccessStatusCode.Should().BeTrue(await picked.Content.ReadAsStringAsync(Ct));
        }

        (await SummaryAsync(_library.MovieTitle)).Watched.Should().BeFalse();
    }

    [Fact]
    public async Task A_session_without_a_profile_is_told_to_pick_one()
    {
        using var client = await _factory.CreateSignedInClientAsync(Ct);

        using var playable = await client.PutAsJsonAsync($"/playables/{_library.Movie}/watched", new { watched = true }, Ct);
        await AssertProblemAsync(playable, HttpStatusCode.Conflict, "profile.required");
        using var title = await client.PutAsJsonAsync($"/titles/{_library.MovieTitle}/watched", new { watched = true }, Ct);
        await AssertProblemAsync(title, HttpStatusCode.Conflict, "profile.required");
    }

    [Fact]
    public async Task Unknown_playables_titles_and_seasons_are_404s()
    {
        using var playable = await _client.PutAsJsonAsync($"/playables/{Guid.NewGuid()}/watched", new { watched = true }, Ct);
        await AssertProblemAsync(playable, HttpStatusCode.NotFound, "playable.not_found");
        using var title = await _client.PutAsJsonAsync($"/titles/{Guid.NewGuid()}/watched", new { watched = true }, Ct);
        await AssertProblemAsync(title, HttpStatusCode.NotFound, "title.not_found");
        using var season = await _client.PutAsJsonAsync($"/titles/{_library.ShowTitle}/watched", new { watched = true, seasonNumber = 5 }, Ct);
        await AssertProblemAsync(season, HttpStatusCode.NotFound, "season.not_found");
        using var movieSeason = await _client.PutAsJsonAsync($"/titles/{_library.MovieTitle}/watched", new { watched = true, seasonNumber = 1 }, Ct);
        await AssertProblemAsync(movieSeason, HttpStatusCode.NotFound, "season.not_found");
    }

    [Fact]
    public async Task A_missing_watched_or_a_negative_season_is_a_bad_request()
    {
        using var playable = await _client.PutAsJsonAsync($"/playables/{_library.Movie}/watched", new { }, Ct);
        await AssertProblemAsync(playable, HttpStatusCode.BadRequest, "watched.invalid");
        using var title = await _client.PutAsJsonAsync($"/titles/{_library.ShowTitle}/watched", new { seasonNumber = 1 }, Ct);
        await AssertProblemAsync(title, HttpStatusCode.BadRequest, "watched.invalid");
        using var negative = await _client.PutAsJsonAsync($"/titles/{_library.ShowTitle}/watched", new { watched = true, seasonNumber = -1 }, Ct);
        await AssertProblemAsync(negative, HttpStatusCode.BadRequest, "watched.invalid");
    }

    private async Task MarkPlayableAsync(Guid playableId, bool watched)
    {
        using var response = await _client.PutAsJsonAsync($"/playables/{playableId}/watched", new { watched }, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync(Ct));
        await ContractDocument.Instance.AssertConformsAsync(response, Ct);
    }

    private async Task MarkTitleAsync(Guid titleId, bool watched, int? seasonNumber = null)
    {
        using var response = await _client.PutAsJsonAsync($"/titles/{titleId}/watched", new { watched, seasonNumber }, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync(Ct));
        await ContractDocument.Instance.AssertConformsAsync(response, Ct);
        _clock.Advance(TimeSpan.FromSeconds(1));
    }

    private async Task ReportAsync(Guid playableId, double position, double duration)
    {
        using var response = await _client.PutAsJsonAsync($"/playables/{playableId}/progress",
            new { positionSeconds = position, durationSeconds = duration }, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        _clock.Advance(TimeSpan.FromSeconds(1));
    }

    private async Task<T> GetAsync<T>(string url)
    {
        using var response = await _client.GetAsync(url, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        await ContractDocument.Instance.AssertConformsAsync(response, Ct);
        return (await response.Content.ReadFromJsonAsync<T>(JsonSerializerOptions.Web, Ct))!;
    }

    private Task<TitleDetail> TitleAsync(Guid titleId) => GetAsync<TitleDetail>($"/titles/{titleId}");

    private async Task<TitleSummary> SummaryAsync(Guid titleId) =>
        (await GetAsync<TitlePage>("/titles")).Items.Single(t => t.Id == titleId);

    private async Task<Dictionary<Guid, bool>> EpisodesWatchedAsync() =>
        (await TitleAsync(_library.ShowTitle)).Seasons!.SelectMany(s => s.Episodes)
            .Where(e => e.PlayableId is not null)
            .ToDictionary(e => e.PlayableId!.Value, e => e.Watched);

    private async Task<int> WatchStateCountAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<MediaManDbContext>().WatchStates.CountAsync(w => w.ProfileId == _profileId, Ct);
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        response.StatusCode.Should().Be(status, await response.Content.ReadAsStringAsync(Ct));
        await ContractDocument.Instance.AssertConformsAsync(response, Ct);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        problem.RootElement.GetProperty("code").GetString().Should().Be(code);
        problem.RootElement.GetProperty("action").GetString().Should().NotBeNullOrWhiteSpace();
    }
}
