using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MediaMan.Core;
using MediaMan.Core.Domain;
using MediaMan.Data;
using MediaMan.Fakes;
using MediaMan.Server.Endpoints;
using MediaMan.Server.LibraryApi;
using MediaMan.Server.Tests.Conformance;
using Microsoft.Extensions.DependencyInjection;

namespace MediaMan.Server.Tests;

/// <summary>
/// <c>TitleDetail.showPlay</c>: what a show's Play starts for the session's profile, picked as the Home hero's
/// (SPEC §7.4, §11).
/// </summary>
public sealed class ShowPlayApiTests : IAsyncLifetime
{
    private readonly FakeClock _clock = new();
    private readonly MediaManFactory _factory;
    private readonly string _media = Path.Combine(Path.GetTempPath(), "mediaman-tests", Guid.NewGuid().ToString("N"));
    private HttpClient _client = null!;
    private HttpClient _noProfile = null!;
    private WatchLibrary _library = null!;

    public ShowPlayApiTests() =>
        _factory = new MediaManFactory { ConfigureServices = services => services.AddSingleton<IClock>(_clock) };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Guid Episode(string key) => _library.Episodes[key];

    public async ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(_media);
        var user = await _factory.CreateUserAsync(Ct, [.. Permissions.All]);
        _client = _factory.CreateClient(user);
        _noProfile = _factory.CreateClient(await _factory.CreateUserAsync(Ct, [.. Permissions.All]));
        var profiles = await _client.GetFromJsonAsync<ProfileList>("/profiles", JsonSerializerOptions.Web, Ct);
        using (var picked = await _client.PutAsJsonAsync("/auth/session/profile", new SelectProfileRequest(profiles!.Items[0].Id), Ct))
        {
            picked.IsSuccessStatusCode.Should().BeTrue(await picked.Content.ReadAsStringAsync(Ct));
        }

        await using var scope = _factory.Services.CreateAsyncScope();
        _library = await WatchLibrary.SeedAsync(scope.ServiceProvider.GetRequiredService<MediaManDbContext>(), _media, Ct);
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        _noProfile.Dispose();
        await _factory.DisposeAsync();
        Directory.Delete(_media, recursive: true);
    }

    [Fact]
    public async Task An_unwatched_show_plays_its_first_regular_episode()
    {
        var play = (await TitleAsync(_client, _library.ShowTitle)).ShowPlay!;

        play.PlayableId.Should().Be(Episode("1x1"));
        (play.Episode.SeasonNumber, play.Episode.Number, play.Episode.Name).Should().Be((1, 1, "Pilot"));
        play.ResumeSeconds.Should().BeNull();
    }

    [Fact]
    public async Task A_part_way_episode_resumes()
    {
        await ReportAsync(Episode("1x1"), 600, 1320);
        await ReportAsync(Episode("1x2"), 750, 1320);

        var play = (await TitleAsync(_client, _library.ShowTitle)).ShowPlay!;

        play.PlayableId.Should().Be(Episode("1x2"));
        play.ResumeSeconds.Should().Be(750);
    }

    [Fact]
    public async Task A_finished_episode_moves_on_to_the_next_one_with_a_file()
    {
        // S01E03's file is missing, so after S01E02 comes S02E01.
        await ReportAsync(Episode("1x2"), 1300, 1320);

        var play = (await TitleAsync(_client, _library.ShowTitle)).ShowPlay!;

        play.PlayableId.Should().Be(Episode("2x1"));
        (play.Episode.SeasonNumber, play.Episode.Number).Should().Be((2, 1));
        play.ResumeSeconds.Should().BeNull();
    }

    [Fact]
    public async Task An_all_watched_show_starts_again_from_the_first_episode()
    {
        using (var marked = await _client.PutAsJsonAsync($"/titles/{_library.ShowTitle}/watched", new { watched = true }, Ct))
        {
            marked.StatusCode.Should().Be(HttpStatusCode.NoContent, await marked.Content.ReadAsStringAsync(Ct));
        }

        var play = (await TitleAsync(_client, _library.ShowTitle)).ShowPlay!;

        play.PlayableId.Should().Be(Episode("1x1"));
        play.ResumeSeconds.Should().BeNull();
    }

    [Fact]
    public async Task Without_a_profile_the_first_episode_plays_without_resume()
    {
        await ReportAsync(Episode("1x2"), 750, 1320);

        var play = (await TitleAsync(_noProfile, _library.ShowTitle)).ShowPlay!;

        play.PlayableId.Should().Be(Episode("1x1"));
        play.ResumeSeconds.Should().BeNull();
    }

    [Fact]
    public async Task A_show_without_episode_files_has_no_show_play()
    {
        var show = new Title { Id = Guid.CreateVersion7(), Kind = TitleKind.Show, Name = "Parks and Recreation" };
        var season = new Season { Id = Guid.CreateVersion7(), TitleId = show.Id, Number = 1 };
        var episode = new Episode { Id = Guid.CreateVersion7(), SeasonId = season.Id, Number = 1, Name = "Pilot" };
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MediaManDbContext>();
            db.AddRange(show, season, episode, Playable.ForEpisode(Guid.CreateVersion7(), episode.Id));
            await db.SaveChangesAsync(Ct);
        }

        (await TitleAsync(_client, show.Id)).ShowPlay.Should().BeNull();
    }

    [Fact]
    public async Task A_movie_has_no_show_play()
    {
        (await TitleAsync(_client, _library.MovieTitle)).ShowPlay.Should().BeNull();
    }

    private async Task ReportAsync(Guid playableId, double position, double duration)
    {
        using var response = await _client.PutAsJsonAsync($"/playables/{playableId}/progress",
            new { positionSeconds = position, durationSeconds = duration }, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        _clock.Advance(TimeSpan.FromSeconds(1));
    }

    private static async Task<TitleDetail> TitleAsync(HttpClient client, Guid titleId)
    {
        using var response = await client.GetAsync($"/titles/{titleId}", Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        await ContractDocument.Instance.AssertConformsAsync(response, Ct);
        return (await response.Content.ReadFromJsonAsync<TitleDetail>(JsonSerializerOptions.Web, Ct))!;
    }
}
