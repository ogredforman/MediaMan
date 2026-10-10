using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MediaMan.Core;
using MediaMan.Core.Domain;
using MediaMan.Data;
using MediaMan.Fakes;
using MediaMan.Server.Endpoints;
using MediaMan.Server.LibraryApi;
using MediaMan.Server.PlaybackApi;
using MediaMan.Server.Tests.Conformance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MediaMan.Server.Tests;

/// <summary>
/// <c>PUT /playables/{playableId}/progress</c>: watch state, the 90% watched mark and the next episode (SPEC §7.4 steps 4–5),
/// and the watch state <c>GET /titles/{titleId}</c> carries for Resume and Start over.
/// </summary>
public sealed class ProgressApiTests : IAsyncLifetime
{
    private readonly FakeClock _clock = new();
    private readonly MediaManFactory _factory;
    private readonly string _media = Path.Combine(Path.GetTempPath(), "mediaman-tests", Guid.NewGuid().ToString("N"));
    private readonly Guid _libraryId = Guid.CreateVersion7();
    private HttpClient _client = null!;
    private Guid _profileId;
    private Guid _movie;
    private Guid _movieTitle;
    private Guid _showTitle;
    private readonly Dictionary<string, Guid> _episodes = [];

    public ProgressApiTests() =>
        _factory = new MediaManFactory { ConfigureServices = services => services.AddSingleton<IClock>(_clock) };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

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
        await SeedAsync(scope.ServiceProvider.GetRequiredService<MediaManDbContext>());
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        Directory.Delete(_media, recursive: true);
    }

    [Fact]
    public async Task Progress_is_stored_for_the_profile_and_the_next_session_resumes_from_it()
    {
        var progress = await ReportAsync(_movie, 1500, 6000);

        progress.Should().Be(new WatchProgressDto(_movie, 1500, 6000, false, null));
        var state = await WatchStateAsync(_movie);
        (state.PositionSeconds, state.DurationSeconds, state.Watched, state.LastWatchedAt).Should().Be((1500, 6000, false, _clock.UtcNow));

        using var session = await _client.PostAsJsonAsync("/playback/sessions", new { playableId = _movie, capabilities = new { player = "mpv" } }, Ct);
        (await session.Content.ReadFromJsonAsync<PlaybackSessionDto>(JsonSerializerOptions.Web, Ct))!.ResumeSeconds.Should().Be(1500);
    }

    [Fact]
    public async Task Ninety_percent_marks_it_watched_and_it_stays_watched()
    {
        (await ReportAsync(_movie, 5400, 6000)).Watched.Should().BeTrue();

        _clock.Advance(TimeSpan.FromMinutes(5));
        var rewound = await ReportAsync(_movie, 30, 6000);

        rewound.Watched.Should().BeTrue();
        rewound.PositionSeconds.Should().Be(30);
        (await WatchStateAsync(_movie)).LastWatchedAt.Should().Be(_clock.UtcNow);
    }

    [Fact]
    public async Task An_episode_names_the_next_episode_with_a_file()
    {
        (await ReportAsync(_episodes["1x1"], 10, 1300)).NextEpisode.Should().Be(
            new NextEpisodeDto(_episodes["1x2"], 1, 2, "The Office S01E02 · Diversity Day"));

        // S01E03's only file is missing, so after S01E02 comes the next season.
        (await ReportAsync(_episodes["1x2"], 10, 1300)).NextEpisode!.PlayableId.Should().Be(_episodes["2x1"]);
        (await ReportAsync(_episodes["0x1"], 10, 1300)).NextEpisode!.PlayableId.Should().Be(_episodes["1x1"]);
        (await ReportAsync(_episodes["2x1"], 10, 1300)).NextEpisode.Should().BeNull();
    }

    [Fact]
    public async Task A_next_episode_whose_file_is_gone_from_disk_is_skipped()
    {
        File.Delete(Path.Combine(_media, "1x2.mkv"));

        (await ReportAsync(_episodes["1x1"], 10, 1300)).NextEpisode!.PlayableId.Should().Be(_episodes["2x1"]);
    }

    [Fact]
    public async Task The_title_detail_offers_resume_where_the_profile_stopped()
    {
        await ReportAsync(_movie, 1500, 6000);
        await ReportAsync(_episodes["1x1"], 600, 1300);
        await ReportAsync(_episodes["1x2"], 90, 1300);
        await ReportAsync(_episodes["2x1"], 1200, 1300);

        var movie = (await TitleAsync(_client, _movieTitle)).Playable!;
        (movie.ResumeSeconds, movie.Watched).Should().Be((1500, false));

        var episodes = (await TitleAsync(_client, _showTitle)).Seasons!.SelectMany(s => s.Episodes)
            .ToDictionary(e => e.PlayableId!.Value, e => (e.ResumeSeconds, e.Watched));
        episodes[_episodes["1x1"]].Should().Be(((double?)600, false));
        episodes[_episodes["1x2"]].Should().Be(((double?)null, false), "under 2 minutes starts from the beginning");
        episodes[_episodes["2x1"]].Should().Be(((double?)null, true), "at 90% it's watched and starts over");
        episodes[_episodes["0x1"]].Should().Be(((double?)null, false), "never played");
    }

    [Fact]
    public async Task A_watched_title_played_again_offers_resume_and_stays_watched()
    {
        await ReportAsync(_movie, 5400, 6000);
        await ReportAsync(_movie, 3000, 6000);

        var movie = (await TitleAsync(_client, _movieTitle)).Playable!;
        (movie.ResumeSeconds, movie.Watched).Should().Be((3000, true));
    }

    [Fact]
    public async Task An_unknown_duration_still_offers_resume()
    {
        await ReportAsync(_movie, 1500, 0);

        (await TitleAsync(_client, _movieTitle)).Playable!.ResumeSeconds.Should().Be(1500);
    }

    [Fact]
    public async Task Another_profile_and_a_session_without_one_see_no_watch_state()
    {
        await ReportAsync(_movie, 5400, 6000);
        await ReportAsync(_movie, 1500, 6000);

        using var noProfile = await _factory.CreateSignedInClientAsync(Ct);
        var unpicked = (await TitleAsync(noProfile, _movieTitle)).Playable!;
        (unpicked.ResumeSeconds, unpicked.Watched).Should().Be(((double?)null, false));

        using (var created = await _client.PostAsJsonAsync("/profiles", new CreateProfileRequest("Kid", null), Ct))
        {
            var other = (await created.Content.ReadFromJsonAsync<ProfileDto>(JsonSerializerOptions.Web, Ct))!.Id;
            using var picked = await _client.PutAsJsonAsync("/auth/session/profile", new SelectProfileRequest(other), Ct);
            picked.IsSuccessStatusCode.Should().BeTrue(await picked.Content.ReadAsStringAsync(Ct));
        }

        var movie = (await TitleAsync(_client, _movieTitle)).Playable!;
        (movie.ResumeSeconds, movie.Watched).Should().Be(((double?)null, false));
    }

    [Theory]
    [InlineData(-1, 6000)]
    [InlineData(10, -1)]
    public async Task Negative_numbers_are_a_bad_request(double position, double duration)
    {
        using var response = await PutAsync(_client, _movie, new { positionSeconds = position, durationSeconds = duration });
        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "progress.invalid");
    }

    [Fact]
    public async Task A_missing_duration_is_a_bad_request()
    {
        using var response = await PutAsync(_client, _movie, new { positionSeconds = 10 });
        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "progress.invalid");
    }

    [Fact]
    public async Task An_empty_body_is_a_bad_request()
    {
        using var content = new StringContent("null", System.Text.Encoding.UTF8, "application/json");
        using var response = await _client.PutAsync(new Uri($"/playables/{_movie}/progress", UriKind.Relative), content, Ct);
        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "progress.invalid");
    }

    [Fact]
    public async Task An_unknown_playable_is_a_404()
    {
        using var response = await PutAsync(_client, Guid.NewGuid(), new { positionSeconds = 10, durationSeconds = 100 });
        await AssertProblemAsync(response, HttpStatusCode.NotFound, "playable.not_found");
    }

    [Fact]
    public async Task A_session_without_a_profile_is_told_to_pick_one()
    {
        using var client = await _factory.CreateSignedInClientAsync(Ct);

        using var response = await PutAsync(client, _movie, new { positionSeconds = 10, durationSeconds = 100 });
        await AssertProblemAsync(response, HttpStatusCode.Conflict, "profile.required");
    }

    private static Task<HttpResponseMessage> PutAsync(HttpClient client, Guid playableId, object body) =>
        client.PutAsJsonAsync($"/playables/{playableId}/progress", body, Ct);

    private async Task<WatchProgressDto> ReportAsync(Guid playableId, double position, double duration)
    {
        using var response = await PutAsync(_client, playableId, new { positionSeconds = position, durationSeconds = duration });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        await ContractDocument.Instance.AssertConformsAsync(response, Ct);
        return (await response.Content.ReadFromJsonAsync<WatchProgressDto>(JsonSerializerOptions.Web, Ct))!;
    }

    private static async Task<TitleDetail> TitleAsync(HttpClient client, Guid titleId)
    {
        using var response = await client.GetAsync($"/titles/{titleId}", Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        await ContractDocument.Instance.AssertConformsAsync(response, Ct);
        return (await response.Content.ReadFromJsonAsync<TitleDetail>(JsonSerializerOptions.Web, Ct))!;
    }

    private async Task<WatchState> WatchStateAsync(Guid playableId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<MediaManDbContext>().WatchStates.AsNoTracking()
            .SingleAsync(w => w.ProfileId == _profileId && w.PlayableId == playableId, Ct);
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        response.StatusCode.Should().Be(status, await response.Content.ReadAsStringAsync(Ct));
        await ContractDocument.Instance.AssertConformsAsync(response, Ct);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        problem.RootElement.GetProperty("code").GetString().Should().Be(code);
        problem.RootElement.GetProperty("action").GetString().Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>A movie, and a show with a special, S01E01–E03 (E03's file missing) and S02E01, keyed "season x episode".</summary>
    private async Task SeedAsync(MediaManDbContext db)
    {
        db.Libraries.Add(new Core.Domain.Library
        {
            Id = _libraryId,
            Name = "Media",
            Kind = LibraryKind.External,
            ContentType = ContentType.Mixed,
            RootPaths = [_media],
        });

        var movie = new Title { Id = Guid.CreateVersion7(), Kind = TitleKind.Movie, Name = "Alien", Year = 1979 };
        var moviePlayable = Playable.ForMovie(Guid.CreateVersion7(), movie.Id);
        db.Titles.Add(movie);
        db.Playables.Add(moviePlayable);
        AddFile(db, moviePlayable.Id, "Alien.mkv", MediaFileStatus.Matched);
        _movie = moviePlayable.Id;
        _movieTitle = movie.Id;

        var show = new Title { Id = Guid.CreateVersion7(), Kind = TitleKind.Show, Name = "The Office", Year = 2005 };
        db.Titles.Add(show);
        _showTitle = show.Id;
        var seasons = new Dictionary<int, Season>();
        foreach (var (season, number, name, status) in new (int, int, string?, MediaFileStatus)[]
        {
            (0, 1, "Special", MediaFileStatus.Matched),
            (1, 1, "Pilot", MediaFileStatus.Matched),
            (1, 2, "Diversity Day", MediaFileStatus.Matched),
            (1, 3, "Health Care", MediaFileStatus.Missing),
            (2, 1, "The Dundies", MediaFileStatus.Matched),
        })
        {
            if (!seasons.TryGetValue(season, out var s))
            {
                s = new Season { Id = Guid.CreateVersion7(), TitleId = show.Id, Number = season };
                seasons[season] = s;
                db.Seasons.Add(s);
            }

            var episode = new Episode { Id = Guid.CreateVersion7(), SeasonId = s.Id, Number = number, Name = name };
            var playable = Playable.ForEpisode(Guid.CreateVersion7(), episode.Id);
            db.Episodes.Add(episode);
            db.Playables.Add(playable);
            AddFile(db, playable.Id, $"{season}x{number}.mkv", status);
            _episodes[$"{season}x{number}"] = playable.Id;
        }

        await db.SaveChangesAsync(Ct);
    }

    private void AddFile(MediaManDbContext db, Guid playableId, string name, MediaFileStatus status)
    {
        var path = Path.Combine(_media, name);
        File.WriteAllText(path, name);
        var file = new MediaFile
        {
            Id = Guid.CreateVersion7(),
            LibraryId = _libraryId,
            Path = path,
            SizeBytes = name.Length,
            Mtime = DateTimeOffset.UnixEpoch,
            Status = status,
            MissingSince = status == MediaFileStatus.Missing ? DateTimeOffset.UnixEpoch : null,
        };
        db.MediaFiles.Add(file);
        db.FileLinks.Add(new FileLink { MediaFileId = file.Id, PlayableId = playableId, Part = 1 });
    }
}
