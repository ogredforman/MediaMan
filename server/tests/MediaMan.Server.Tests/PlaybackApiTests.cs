using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MediaMan.Auth;
using MediaMan.Core;
using MediaMan.Core.Domain;
using MediaMan.Data;
using MediaMan.Playback.Subtitles;
using MediaMan.Server.Endpoints;
using MediaMan.Server.LibraryApi;
using MediaMan.Server.PlaybackApi;
using MediaMan.Server.Tests.Conformance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MediaMan.Server.Tests;

/// <summary><c>POST /playback/sessions</c> for MPV, and the session-signed stream and subtitle URLs it returns (SPEC §7.4).</summary>
public sealed class PlaybackApiTests : IAsyncLifetime
{
    private const string UhdBytes = "0123456789abcdef";
    private static readonly string[] _browserContainers = ["mp4"];

    private readonly MediaManFactory _factory = new();
    private readonly string _media = Path.Combine(Path.GetTempPath(), "mediaman-tests", Guid.NewGuid().ToString("N"));
    private TestUser _user = null!;
    private HttpClient _client = null!;
    private Guid _profileId;
    private Seed _seed = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(_media);
        _user = await _factory.CreateUserAsync(Ct, [.. Permissions.All]);
        _client = _factory.CreateClient(_user);
        _profileId = await PickProfileAsync(_client);
        await using var scope = _factory.Services.CreateAsyncScope();
        _seed = await Seed.CreateAsync(scope.ServiceProvider.GetRequiredService<MediaManDbContext>(), _media, Ct);
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        Directory.Delete(_media, recursive: true);
    }

    [Fact]
    public async Task Mpv_gets_a_signed_direct_stream_of_the_highest_quality()
    {
        var session = await CreateAsync(_client, _seed.MoviePlayableId);

        session.MediaFileId.Should().Be(_seed.MovieUhd);
        session.Method.Should().Be("directStream");
        session.Title.Should().Be("Alien (1979)");
        session.DurationSeconds.Should().Be(6000);
        session.ResumeSeconds.Should().BeNull();
        session.Url.Should().StartWith($"/media-files/{_seed.MovieUhd}/stream?sid={_user.SessionId:N}&exp=");
    }

    [Fact]
    public async Task The_profiles_preferred_version_plays()
    {
        using (var set = await _client.PutAsJsonAsync($"/titles/{_seed.MovieId}/preferred-version", new PreferredVersionRequest(_seed.MovieHd), Ct))
        {
            set.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        (await CreateAsync(_client, _seed.MoviePlayableId)).MediaFileId.Should().Be(_seed.MovieHd);
    }

    [Fact]
    public async Task The_stream_loads_without_a_token_and_supports_ranges()
    {
        var session = await CreateAsync(_client, _seed.MoviePlayableId);
        using var player = _factory.CreateClient();

        using (var whole = await player.GetAsync(new Uri(session.Url, UriKind.Relative), Ct))
        {
            whole.StatusCode.Should().Be(HttpStatusCode.OK);
            whole.Headers.AcceptRanges.Should().Contain("bytes");
            whole.Content.Headers.ContentType!.MediaType.Should().Be("video/x-matroska");
            (await whole.Content.ReadAsStringAsync(Ct)).Should().Be(UhdBytes);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(session.Url, UriKind.Relative));
        request.Headers.Range = new RangeHeaderValue(4, 7);
        using var part = await player.SendAsync(request, Ct);
        part.StatusCode.Should().Be(HttpStatusCode.PartialContent);
        part.Content.Headers.ContentRange!.ToString().Should().Be($"bytes 4-7/{UhdBytes.Length}");
        (await part.Content.ReadAsStringAsync(Ct)).Should().Be("4567");
    }

    [Fact]
    public async Task The_stream_stops_working_when_the_session_is_revoked()
    {
        var session = await CreateAsync(_client, _seed.MoviePlayableId);
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<SessionService>().RevokeAsync(_user.SessionId, Ct);
        }

        using var player = _factory.CreateClient();
        using var refused = await player.GetAsync(new Uri(session.Url, UriKind.Relative), Ct);
        await AssertProblemAsync(refused, HttpStatusCode.Unauthorized, "auth.session_expired");
    }

    [Fact]
    public async Task External_subtitles_are_listed_and_served()
    {
        var session = await CreateAsync(_client, _seed.MoviePlayableId);

        session.Subtitles.Select(s => (s.FileName, s.Format, s.Language, s.Forced)).Should().Equal(
            ("Alien.2160p.en.srt", "srt", "en", false),
            ("Alien.2160p.fr.forced.ass", "ass", "fr", true));
        session.Subtitles.Should().AllSatisfy(s => s.Url.Should().Contain($"sid={_user.SessionId:N}"));

        using var player = _factory.CreateClient();
        using var srt = await player.GetAsync(new Uri(session.Subtitles[0].Url, UriKind.Relative), Ct);
        srt.StatusCode.Should().Be(HttpStatusCode.OK);
        srt.Content.Headers.ContentType!.MediaType.Should().Be("application/x-subrip");
        (await srt.Content.ReadAsStringAsync(Ct)).Should().Contain("Hello");
    }

    [Fact]
    public async Task Only_a_current_sidecar_of_the_file_is_served()
    {
        using var unknown = await _client.GetAsync(new Uri(PlaybackService.SubtitlePathOf(_seed.MovieUhd, "0000000000000000"), UriKind.Relative), Ct);
        await AssertProblemAsync(unknown, HttpStatusCode.NotFound, "subtitle.not_found");

        // Another file's subtitle, by its real id, isn't served through this file.
        var otherId = SidecarSubtitles.IdOf("Alien.2160p.en.srt");
        using var foreign = await _client.GetAsync(new Uri(PlaybackService.SubtitlePathOf(_seed.MovieHd, otherId), UriKind.Relative), Ct);
        await AssertProblemAsync(foreign, HttpStatusCode.NotFound, "subtitle.not_found");

        using var traversal = await _client.GetAsync(new Uri($"/media-files/{_seed.MovieUhd}/subtitles/..%2F..%2Fsecret.srt", UriKind.Relative), Ct);
        traversal.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData(300, 300.0)]
    [InlineData(60, null)]
    [InlineData(5500, null)]
    public async Task Resume_is_offered_after_two_minutes_and_before_ninety_percent(double position, double? expected)
    {
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MediaManDbContext>();
            db.WatchStates.Add(new WatchState
            {
                ProfileId = _profileId,
                PlayableId = _seed.MoviePlayableId,
                PositionSeconds = position,
                DurationSeconds = 6000,
                LastWatchedAt = DateTimeOffset.UnixEpoch,
            });
            await db.SaveChangesAsync(Ct);
        }

        (await CreateAsync(_client, _seed.MoviePlayableId)).ResumeSeconds.Should().Be(expected);
    }

    [Fact]
    public async Task An_episode_is_titled_with_its_show_and_number()
    {
        (await CreateAsync(_client, _seed.EpisodePlayableId)).Title.Should().Be("The Office S01E02 · Diversity Day");
    }

    [Fact]
    public async Task A_session_carries_no_language_tags_without_preferences()
    {
        var session = await CreateAsync(_client, _seed.MoviePlayableId);

        session.AudioLanguages.Should().BeEmpty();
        session.SubtitleLanguages.Should().BeEmpty();
    }

    [Fact]
    public async Task A_session_carries_the_profile_languages_as_match_tags_in_preference_order()
    {
        using var patched = await _client.PatchAsJsonAsync(
            $"/profiles/{_profileId}", new UpdateProfileRequest(null, null, ["ja", "en"], ["de"]), Ct);
        patched.IsSuccessStatusCode.Should().BeTrue(await patched.Content.ReadAsStringAsync(Ct));

        var session = await CreateAsync(_client, _seed.MoviePlayableId);

        session.AudioLanguages.Should().Equal("ja", "jpn", "en", "eng");
        session.SubtitleLanguages.Should().Equal("de", "ger", "deu");
    }

    [Fact]
    public void Match_tags_skip_unknown_codes()
    {
        PlaybackService.MatchTagsOf(["xx", "fr"]).Should().Equal("fr", "fre", "fra");
    }

    [Fact]
    public async Task A_browser_is_told_to_use_the_desktop_app_for_now()
    {
        using var response = await PostAsync(_client, new { playableId = _seed.MoviePlayableId, capabilities = new { player = "browser", containers = _browserContainers } });
        await AssertProblemAsync(response, HttpStatusCode.NotImplemented, "playback.player_unsupported");
    }

    [Fact]
    public async Task An_unknown_player_is_a_bad_request()
    {
        using var response = await PostAsync(_client, new { playableId = _seed.MoviePlayableId, capabilities = new { player = "vlc" } });
        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "playback.invalid");
    }

    [Fact]
    public async Task An_unknown_playable_is_a_404()
    {
        using var response = await PostAsync(_client, Mpv(Guid.NewGuid()));
        await AssertProblemAsync(response, HttpStatusCode.NotFound, "playable.not_found");
    }

    [Fact]
    public async Task A_playable_with_no_file_on_disk_has_nothing_to_play()
    {
        File.Delete(Path.Combine(_media, "Office.S01E02.mkv"));

        using var response = await PostAsync(_client, Mpv(_seed.EpisodePlayableId));
        await AssertProblemAsync(response, HttpStatusCode.Conflict, "playback.no_file");
    }

    [Fact]
    public async Task A_session_without_a_profile_is_told_to_pick_one()
    {
        using var client = await _factory.CreateSignedInClientAsync(Ct);

        using var response = await PostAsync(client, Mpv(_seed.MoviePlayableId));
        await AssertProblemAsync(response, HttpStatusCode.Conflict, "profile.required");
    }

    [Fact]
    public async Task Missing_or_unlinked_files_are_not_streamed()
    {
        foreach (var id in new[] { _seed.MovieMissing, _seed.Unlinked, Guid.NewGuid() })
        {
            using var response = await _client.GetAsync(new Uri(PlaybackService.StreamPathOf(id), UriKind.Relative), Ct);
            await AssertProblemAsync(response, HttpStatusCode.NotFound, "media_file.unavailable");
        }
    }

    private static object Mpv(Guid playableId) => new { playableId, capabilities = new { player = "mpv" } };

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, object body) =>
        client.PostAsJsonAsync("/playback/sessions", body, Ct);

    private static async Task<PlaybackSessionDto> CreateAsync(HttpClient client, Guid playableId)
    {
        using var response = await PostAsync(client, Mpv(playableId));
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        await ContractDocument.Instance.AssertConformsAsync(response, Ct);
        return (await response.Content.ReadFromJsonAsync<PlaybackSessionDto>(JsonSerializerOptions.Web, Ct))!;
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        response.StatusCode.Should().Be(status, await response.Content.ReadAsStringAsync(Ct));
        await ContractDocument.Instance.AssertConformsAsync(response, Ct);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        problem.RootElement.GetProperty("code").GetString().Should().Be(code);
        problem.RootElement.GetProperty("action").GetString().Should().NotBeNullOrWhiteSpace();
    }

    private static async Task<Guid> PickProfileAsync(HttpClient client)
    {
        var profiles = await client.GetFromJsonAsync<ProfileList>("/profiles", JsonSerializerOptions.Web, Ct);
        var profileId = profiles!.Items[0].Id;
        using var picked = await client.PutAsJsonAsync("/auth/session/profile", new SelectProfileRequest(profileId), Ct);
        picked.IsSuccessStatusCode.Should().BeTrue(await picked.Content.ReadAsStringAsync(Ct));
        return profileId;
    }

    /// <summary>
    /// Files on disk: a movie in 1080p and 2160p (with two subtitles next to the 2160p) plus a missing 720p, an episode,
    /// and a file that holds no Playable.
    /// </summary>
    private sealed class Seed
    {
        private readonly Guid _libraryId = Guid.CreateVersion7();
        private string _root = "";

        public Guid MovieId { get; private set; }
        public Guid MoviePlayableId { get; private set; }
        public Guid MovieHd { get; private set; }
        public Guid MovieUhd { get; private set; }
        public Guid MovieMissing { get; private set; }
        public Guid EpisodePlayableId { get; private set; }
        public Guid Unlinked { get; private set; }

        public static async Task<Seed> CreateAsync(MediaManDbContext db, string root, CancellationToken ct)
        {
            var seed = new Seed { _root = root };
            db.Libraries.Add(new Core.Domain.Library
            {
                Id = seed._libraryId,
                Name = "Media",
                Kind = LibraryKind.External,
                ContentType = ContentType.Mixed,
                RootPaths = [root],
            });

            var movie = new Title { Id = Guid.CreateVersion7(), Kind = TitleKind.Movie, Name = "Alien", Year = 1979 };
            var playable = Playable.ForMovie(Guid.CreateVersion7(), movie.Id);
            db.Titles.Add(movie);
            db.Playables.Add(playable);
            seed.MovieId = movie.Id;
            seed.MoviePlayableId = playable.Id;
            seed.MovieHd = seed.AddFile(db, playable.Id, "Alien.1080p.mkv", 1920, MediaFileStatus.Matched, "hd");
            seed.MovieUhd = seed.AddFile(db, playable.Id, "Alien.2160p.mkv", 3840, MediaFileStatus.Matched, UhdBytes);
            seed.MovieMissing = seed.AddFile(db, playable.Id, "Alien.720p.mkv", 1280, MediaFileStatus.Missing, "sd");
            File.WriteAllText(Path.Combine(root, "Alien.2160p.en.srt"), "1\n00:00:01,000 --> 00:00:02,000\nHello\n");
            File.WriteAllText(Path.Combine(root, "Alien.2160p.fr.forced.ass"), "[Script Info]\n");

            var show = new Title { Id = Guid.CreateVersion7(), Kind = TitleKind.Show, Name = "The Office", Year = 2005 };
            var season = new Season { Id = Guid.CreateVersion7(), TitleId = show.Id, Number = 1 };
            var episode = new Episode { Id = Guid.CreateVersion7(), SeasonId = season.Id, Number = 2, Name = "Diversity Day" };
            var episodePlayable = Playable.ForEpisode(Guid.CreateVersion7(), episode.Id);
            db.Titles.Add(show);
            db.Seasons.Add(season);
            db.Episodes.Add(episode);
            db.Playables.Add(episodePlayable);
            seed.EpisodePlayableId = episodePlayable.Id;
            seed.AddFile(db, episodePlayable.Id, "Office.S01E02.mkv", 1920, MediaFileStatus.Matched, "ep");

            seed.Unlinked = seed.AddFile(db, null, "Unknown.mkv", 1920, MediaFileStatus.NeedsAttention, "??");

            await db.SaveChangesAsync(ct);
            return seed;
        }

        private Guid AddFile(MediaManDbContext db, Guid? playableId, string name, int width, MediaFileStatus status, string content)
        {
            var path = Path.Combine(_root, name);
            File.WriteAllText(path, content);
            var file = new MediaFile
            {
                Id = Guid.CreateVersion7(),
                LibraryId = _libraryId,
                Path = path,
                SizeBytes = content.Length,
                Mtime = DateTimeOffset.UnixEpoch,
                Status = status,
                MissingSince = status == MediaFileStatus.Missing ? DateTimeOffset.UnixEpoch : null,
                Probe = new ProbeResult
                {
                    Container = "matroska",
                    Duration = TimeSpan.FromSeconds(6000),
                    Streams = [new StreamInfo { Index = 0, Type = MediaStreamType.Video, Codec = "hevc", Width = width, Height = width * 9 / 16 }],
                },
            };
            db.MediaFiles.Add(file);
            if (playableId is { } id)
            {
                db.FileLinks.Add(new FileLink { MediaFileId = file.Id, PlayableId = id, Part = 1 });
            }

            return file.Id;
        }
    }
}
