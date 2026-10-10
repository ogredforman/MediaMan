using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MediaMan.Core.Domain;
using MediaMan.Data;
using MediaMan.Server.Endpoints;
using MediaMan.Server.LibraryApi;
using MediaMan.Server.Tests.Conformance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MediaMan.Server.Tests;

/// <summary>The detail page's version picker: preferred versions per profile and title, and what Play would use.</summary>
public sealed class VersionPreferenceApiTests : IAsyncLifetime
{
    private readonly MediaManFactory _factory = new();
    private HttpClient _client = null!;
    private Seed _seed = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _client = await _factory.CreateSignedInClientAsync(Ct);
        await PickProfileAsync(_client, create: null);
        await using var scope = _factory.Services.CreateAsyncScope();
        _seed = await Seed.CreateAsync(scope.ServiceProvider.GetRequiredService<MediaManDbContext>(), Ct);
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task Without_a_preference_the_highest_quality_is_selected()
    {
        var movie = await GetTitleAsync(_client, _seed.MovieId);

        movie.VersionPreference.Should().BeNull();
        movie.Playable!.SelectedMediaFileId.Should().Be(_seed.MovieUhd);

        var show = await GetTitleAsync(_client, _seed.ShowId);
        show.Seasons![0].Episodes.Select(e => e.SelectedMediaFileId).Should().Equal(_seed.UhdEpisodes.Select(id => (Guid?)id));
    }

    [Fact]
    public async Task A_picked_movie_version_is_selected_until_cleared()
    {
        await SetAsync(_client, _seed.MovieId, _seed.MovieHd, HttpStatusCode.NoContent);

        var movie = await GetTitleAsync(_client, _seed.MovieId);
        movie.Playable!.SelectedMediaFileId.Should().Be(_seed.MovieHd);
        movie.VersionPreference.Should().Be(new VersionPreferenceDto(_seed.MovieHd, "1080p", null));

        using (var cleared = await _client.DeleteAsync(PreferencePath(_seed.MovieId), Ct))
        {
            cleared.StatusCode.Should().Be(HttpStatusCode.NoContent);
            await ContractDocument.Instance.AssertConformsAsync(cleared, Ct);
        }

        movie = await GetTitleAsync(_client, _seed.MovieId);
        movie.Playable!.SelectedMediaFileId.Should().Be(_seed.MovieUhd);
        movie.VersionPreference.Should().BeNull();
    }

    [Fact]
    public async Task Picking_again_replaces_the_preference()
    {
        await SetAsync(_client, _seed.MovieId, _seed.MovieHd, HttpStatusCode.NoContent);
        await SetAsync(_client, _seed.MovieId, _seed.MovieUhd, HttpStatusCode.NoContent);

        var movie = await GetTitleAsync(_client, _seed.MovieId);
        movie.VersionPreference.Should().Be(new VersionPreferenceDto(_seed.MovieUhd, "2160p", "HDR10"));
    }

    [Fact]
    public async Task A_version_picked_on_one_episode_carries_to_the_others_by_quality()
    {
        await SetAsync(_client, _seed.ShowId, _seed.HdEpisodes[0], HttpStatusCode.NoContent);

        var show = await GetTitleAsync(_client, _seed.ShowId);
        show.Seasons![0].Episodes.Select(e => e.SelectedMediaFileId).Should().Equal(_seed.HdEpisodes.Select(id => (Guid?)id));
    }

    [Fact]
    public async Task Each_profile_keeps_its_own_choice()
    {
        await SetAsync(_client, _seed.MovieId, _seed.MovieHd, HttpStatusCode.NoContent);

        // Same account, another profile picked on its own session.
        var other = _factory.CreateClient();
        other.DefaultRequestHeaders.Authorization = _client.DefaultRequestHeaders.Authorization;
        await PickProfileAsync(other, create: "Guest");

        var movie = await GetTitleAsync(other, _seed.MovieId);
        movie.Playable!.SelectedMediaFileId.Should().Be(_seed.MovieUhd);
        movie.VersionPreference.Should().BeNull();
    }

    [Fact]
    public async Task A_preference_outlives_its_file_and_still_applies_by_quality()
    {
        await SetAsync(_client, _seed.ShowId, _seed.HdEpisodes[0], HttpStatusCode.NoContent);
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MediaManDbContext>();
            await db.MediaFiles.Where(f => f.Id == _seed.HdEpisodes[0]).ExecuteDeleteAsync(Ct);
        }

        var show = await GetTitleAsync(_client, _seed.ShowId);
        show.VersionPreference.Should().Be(new VersionPreferenceDto(null, "1080p", null));
        show.Seasons![0].Episodes[1].SelectedMediaFileId.Should().Be(_seed.HdEpisodes[1]);
    }

    [Fact]
    public async Task Playback_finds_the_preference_from_a_movie_or_an_episode_playable()
    {
        await SetAsync(_client, _seed.MovieId, _seed.MovieHd, HttpStatusCode.NoContent);
        await SetAsync(_client, _seed.ShowId, _seed.HdEpisodes[0], HttpStatusCode.NoContent);
        var profileId = (await _client.GetFromJsonAsync<ProfileList>("/profiles", JsonSerializerOptions.Web, Ct))!.ActiveProfileId;

        await using var scope = _factory.Services.CreateAsyncScope();
        var preferences = scope.ServiceProvider.GetRequiredService<VersionPreferenceService>();
        (await preferences.ForPlayableAsync(profileId, _seed.MoviePlayableId, Ct))!.MediaFileId.Should().Be(_seed.MovieHd);
        (await preferences.ForPlayableAsync(profileId, _seed.EpisodePlayableIds[1], Ct))!.MediaFileId.Should().Be(_seed.HdEpisodes[0]);
        (await preferences.ForPlayableAsync(null, _seed.MoviePlayableId, Ct)).Should().BeNull();
        (await preferences.ForPlayableAsync(profileId, Guid.NewGuid(), Ct)).Should().BeNull();
    }

    [Fact]
    public async Task Only_an_available_version_of_the_title_can_be_picked()
    {
        await AssertProblemAsync(await SetAsync(_client, _seed.MovieId, _seed.MovieMissing, HttpStatusCode.BadRequest), "version.invalid");
        await AssertProblemAsync(await SetAsync(_client, _seed.MovieId, _seed.HdEpisodes[0], HttpStatusCode.BadRequest), "version.invalid");
        await AssertProblemAsync(await SetAsync(_client, _seed.MovieId, Guid.NewGuid(), HttpStatusCode.BadRequest), "version.invalid");

        (await GetTitleAsync(_client, _seed.MovieId)).VersionPreference.Should().BeNull();
    }

    [Fact]
    public async Task An_unknown_title_is_a_404()
    {
        await AssertProblemAsync(await SetAsync(_client, Guid.NewGuid(), _seed.MovieHd, HttpStatusCode.NotFound), "title.not_found");

        using var cleared = await _client.DeleteAsync(PreferencePath(Guid.NewGuid()), Ct);
        cleared.StatusCode.Should().Be(HttpStatusCode.NotFound);
        await AssertProblemAsync(cleared, "title.not_found");
    }

    [Fact]
    public async Task A_session_without_a_profile_is_told_to_pick_one()
    {
        using var client = await _factory.CreateSignedInClientAsync(Ct);

        await AssertProblemAsync(await SetAsync(client, _seed.MovieId, _seed.MovieHd, HttpStatusCode.Conflict), "profile.required");
        using var cleared = await client.DeleteAsync(PreferencePath(_seed.MovieId), Ct);
        cleared.StatusCode.Should().Be(HttpStatusCode.Conflict);
        await AssertProblemAsync(cleared, "profile.required");

        // Browsing still works; Play falls back to the highest quality.
        (await GetTitleAsync(client, _seed.MovieId)).Playable!.SelectedMediaFileId.Should().Be(_seed.MovieUhd);
    }

    private static Uri PreferencePath(Guid titleId) => new($"/titles/{titleId}/preferred-version", UriKind.Relative);

    private static async Task<HttpResponseMessage> SetAsync(HttpClient client, Guid titleId, Guid mediaFileId, HttpStatusCode expected)
    {
        var response = await client.PutAsJsonAsync(PreferencePath(titleId), new PreferredVersionRequest(mediaFileId), Ct);
        response.StatusCode.Should().Be(expected, await response.Content.ReadAsStringAsync(Ct));
        await ContractDocument.Instance.AssertConformsAsync(response, Ct);
        return response;
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, string code)
    {
        using (response)
        {
            using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
            problem.RootElement.GetProperty("code").GetString().Should().Be(code);
            problem.RootElement.GetProperty("action").GetString().Should().NotBeNullOrWhiteSpace();
        }
    }

    private static async Task<TitleDetail> GetTitleAsync(HttpClient client, Guid titleId)
    {
        using var response = await client.GetAsync(new Uri($"/titles/{titleId}", UriKind.Relative), Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        await ContractDocument.Instance.AssertConformsAsync(response, Ct);
        return (await response.Content.ReadFromJsonAsync<TitleDetail>(JsonSerializerOptions.Web, Ct))!;
    }

    /// <summary>Picks the account's default profile, or creates <paramref name="create"/> and picks that.</summary>
    private static async Task PickProfileAsync(HttpClient client, string? create)
    {
        Guid profileId;
        if (create is null)
        {
            var profiles = await client.GetFromJsonAsync<ProfileList>("/profiles", JsonSerializerOptions.Web, Ct);
            profileId = profiles!.Items[0].Id;
        }
        else
        {
            using var created = await client.PostAsJsonAsync("/profiles", new CreateProfileRequest(create, null), Ct);
            created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(Ct));
            profileId = (await created.Content.ReadFromJsonAsync<ProfileDto>(JsonSerializerOptions.Web, Ct))!.Id;
        }

        using var picked = await client.PutAsJsonAsync("/auth/session/profile", new SelectProfileRequest(profileId), Ct);
        picked.IsSuccessStatusCode.Should().BeTrue(await picked.Content.ReadAsStringAsync(Ct));
    }

    /// <summary>A movie with 1080p, 2160p HDR10 and a missing 720p version; a show whose two episodes each have 1080p and 2160p.</summary>
    private sealed class Seed
    {
        private readonly Guid _libraryId = Guid.CreateVersion7();

        public Guid MovieId { get; private set; }
        public Guid MoviePlayableId { get; private set; }
        public Guid MovieHd { get; private set; }
        public Guid MovieUhd { get; private set; }
        public Guid MovieMissing { get; private set; }
        public Guid ShowId { get; private set; }
        public List<Guid> EpisodePlayableIds { get; } = [];
        public List<Guid> HdEpisodes { get; } = [];
        public List<Guid> UhdEpisodes { get; } = [];

        public static async Task<Seed> CreateAsync(MediaManDbContext db, CancellationToken ct)
        {
            var seed = new Seed();
            db.Libraries.Add(new Core.Domain.Library
            {
                Id = seed._libraryId,
                Name = "Media",
                Kind = LibraryKind.External,
                ContentType = ContentType.Mixed,
                RootPaths = [Root()],
            });

            var movie = new Title { Id = Guid.CreateVersion7(), Kind = TitleKind.Movie, Name = "Alien", Year = 1979 };
            var playable = Playable.ForMovie(Guid.CreateVersion7(), movie.Id);
            db.Titles.Add(movie);
            db.Playables.Add(playable);
            seed.MovieId = movie.Id;
            seed.MoviePlayableId = playable.Id;
            seed.MovieHd = seed.AddFile(db, playable.Id, 1, "Alien.1080p.mkv", 1920, 1080, null, MediaFileStatus.Matched);
            seed.MovieUhd = seed.AddFile(db, playable.Id, 1, "Alien.2160p.mkv", 3840, 2160, "HDR10", MediaFileStatus.Matched);
            seed.MovieMissing = seed.AddFile(db, playable.Id, 1, "Alien.720p.mkv", 1280, 720, null, MediaFileStatus.Missing);

            var show = new Title { Id = Guid.CreateVersion7(), Kind = TitleKind.Show, Name = "The Office", Year = 2005 };
            var season = new Season { Id = Guid.CreateVersion7(), TitleId = show.Id, Number = 1 };
            db.Titles.Add(show);
            db.Seasons.Add(season);
            seed.ShowId = show.Id;
            foreach (var number in new[] { 1, 2 })
            {
                var episode = new Episode { Id = Guid.CreateVersion7(), SeasonId = season.Id, Number = number };
                var episodePlayable = Playable.ForEpisode(Guid.CreateVersion7(), episode.Id);
                db.Episodes.Add(episode);
                db.Playables.Add(episodePlayable);
                seed.EpisodePlayableIds.Add(episodePlayable.Id);
                // The second episode's 1080p is a scope encode: still 1080p by width.
                seed.HdEpisodes.Add(seed.AddFile(db, episodePlayable.Id, 1, $"Office.S01E0{number}.1080p.mkv", 1920, number == 1 ? 1080 : 800, null, MediaFileStatus.Matched));
                seed.UhdEpisodes.Add(seed.AddFile(db, episodePlayable.Id, 1, $"Office.S01E0{number}.2160p.mkv", 3840, 2160, null, MediaFileStatus.Matched));
            }

            await db.SaveChangesAsync(ct);
            return seed;
        }

        private Guid AddFile(
            MediaManDbContext db, Guid playableId, int part, string name, int width, int height, string? hdr, MediaFileStatus status)
        {
            var file = new MediaFile
            {
                Id = Guid.CreateVersion7(),
                LibraryId = _libraryId,
                Path = Path.Combine(Root(), name),
                SizeBytes = 1_000_000,
                Mtime = DateTimeOffset.UnixEpoch,
                Status = status,
                MissingSince = status == MediaFileStatus.Missing ? DateTimeOffset.UnixEpoch : null,
                Probe = new ProbeResult
                {
                    Container = "matroska",
                    HdrFormat = hdr,
                    Streams = [new StreamInfo { Index = 0, Type = MediaStreamType.Video, Codec = "hevc", Width = width, Height = height }],
                },
            };
            db.MediaFiles.Add(file);
            db.FileLinks.Add(new FileLink { MediaFileId = file.Id, PlayableId = playableId, Part = part });
            return file.Id;
        }

        private static string Root() => Path.Combine(Path.GetTempPath(), "mediaman-version-preferences");
    }
}
