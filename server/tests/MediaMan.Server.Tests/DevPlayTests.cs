using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MediaMan.Core.Domain;
using MediaMan.Data;
using MediaMan.Server.Dev;
using MediaMan.Server.Tests.Conformance;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaMan.Server.Tests;

/// <summary>
/// The dev-only play endpoint (SPEC §8 playback [P], §13). It exists only in the dev configuration of a Debug build,
/// so these tests need a Debug test run, like <see cref="DevConfigurationTests"/>. Nothing here launches a real MPV.
/// </summary>
public sealed class DevPlayTests : IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "mediaman-tests", Guid.NewGuid().ToString("N"));
    private MediaManFactory _factory = null!;
    private HttpClient _client = null!;
    private FakeDevPlayer _player = null!;

    private string MediaPath => Path.Combine(_root, "Media");

    public async ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(MediaPath);
        _factory = DevFactory(new() { ["MediaMan:Dev:Providers:Player"] = "Fake" });
        _client = await _factory.CreateSignedInClientAsync(Ct);
        _player = (FakeDevPlayer)_factory.Services.GetRequiredService<IDevPlayer>();
        await using var scope = _factory.Services.CreateAsyncScope();
        await SeedAsync(scope.ServiceProvider.GetRequiredService<MediaManDbContext>());
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        Directory.Delete(_root, recursive: true);
    }

    private MediaManFactory DevFactory(Dictionary<string, string>? extra = null)
    {
        var settings = new Dictionary<string, string> { ["MediaMan:Dev:MediaPath"] = MediaPath };
        foreach (var (key, value) in extra ?? [])
        {
            settings[key] = value;
        }

        return new MediaManFactory("Development", settings);
    }

    private Guid _alienPlayableId;
    private Guid _alienBestFileId;
    private Guid _ghostPlayableId;
    private Guid _episodeTwoPlayableId;

    [Fact]
    public async Task Playing_a_movie_launches_the_best_available_version()
    {
        using var response = await PlayAsync(_alienPlayableId);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        await ContractDocument.Instance.AssertConformsAsync(response, Ct);
        var body = (await response.Content.ReadFromJsonAsync<DevPlayStarted>(JsonSerializerOptions.Web, Ct))!;
        body.Should().Be(new DevPlayStarted(_alienBestFileId, "Alien.1979.1080p.mkv"));
        _player.Plays.Should().Equal((Path.Combine(MediaPath, "Alien.1979.1080p.mkv"), "Alien (1979)"));
    }

    [Fact]
    public async Task Playing_an_episode_of_a_multi_episode_file_launches_that_file()
    {
        using var response = await PlayAsync(_episodeTwoPlayableId);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        _player.Plays.Should().Equal((Path.Combine(MediaPath, "The.Office.S01E01-E02.mkv"), "The Office S01E02 · Episode 2"));
    }

    [Fact]
    public async Task An_unknown_playable_is_not_found()
    {
        using var response = await PlayAsync(Guid.NewGuid());

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "playable.not_found");
        _player.Plays.Should().BeEmpty();
    }

    [Fact]
    public async Task A_playable_whose_files_are_all_missing_is_a_conflict()
    {
        using var response = await PlayAsync(_ghostPlayableId);

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "playback.no_file");
        _player.Plays.Should().BeEmpty();
    }

    [Fact]
    public async Task A_player_that_cannot_start_is_unavailable_with_the_fix()
    {
        _player.FailNext("mpv not found");

        using var response = await PlayAsync(_alienPlayableId);

        var body = await AssertProblemAsync(response, HttpStatusCode.ServiceUnavailable, "playback.player_unavailable");
        body.GetProperty("action").GetString().Should().Contain("MediaMan:Tools:MpvPath");
    }

    [Fact]
    public async Task The_endpoint_is_mapped_with_the_contract_permission_in_the_dev_configuration()
    {
        await using var factory = DevFactory();
        var routes = await ContractCoverageTests.MappedRoutesAsync(factory);

        foreach (var operation in ContractDocument.Instance.Operations.Where(o => o.DevOnly))
        {
            routes.Should().Contain((operation.Method, operation.Path, operation.Permission, false));
        }

        factory.Services.GetRequiredService<IDevPlayer>().Should().BeOfType<MpvDevPlayer>("MPV is the default player");
    }

    [Theory]
    [InlineData("Testing", true)]
    [InlineData("Development", false)]
    public async Task Outside_the_dev_configuration_the_endpoint_is_not_found(string environment, bool withMediaPath)
    {
        var settings = withMediaPath ? new Dictionary<string, string> { ["MediaMan:Dev:MediaPath"] = MediaPath } : null;
        await using var factory = new MediaManFactory(environment, settings);
        using var client = factory.CreateClient();

        using var response = await client.PostAsync(new Uri($"/dev/playables/{_alienPlayableId}/play", UriKind.Relative), null, Ct);

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "not_found");
    }

    [Fact]
    public async Task Mpv_that_cannot_be_found_fails_with_a_player_error()
    {
        var player = new MpvDevPlayer(Path.Combine(_root, "no-such-mpv"), NullLogger<MpvDevPlayer>.Instance);

        var play = () => player.PlayAsync(Path.Combine(MediaPath, "Alien.1979.1080p.mkv"), "Alien", Ct);

        await play.Should().ThrowAsync<DevPlayerException>().WithMessage("*no-such-mpv*couldn't be started*");
    }

    private Task<HttpResponseMessage> PlayAsync(Guid playableId) =>
        _client.PostAsync(new Uri($"/dev/playables/{playableId}/play", UriKind.Relative), null, Ct);

    private static async Task<JsonElement> AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        response.StatusCode.Should().Be(status);
        await ContractDocument.Instance.AssertConformsAsync(response, Ct);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        body.RootElement.GetProperty("code").GetString().Should().Be(code);
        body.RootElement.GetProperty("action").GetString().Should().NotBeNullOrWhiteSpace();
        return body.RootElement.Clone();
    }

    /// <summary>
    /// Alien: a 720p file, a 2160p file that's Missing and a 1080p file (the pick). Ghost: only a Missing file.
    /// The Office: episodes 1 and 2 in one file. Files that should be playable exist on disk as empty placeholders.
    /// </summary>
    private async Task SeedAsync(MediaManDbContext db)
    {
        var library = new Core.Domain.Library
        {
            Id = Guid.CreateVersion7(),
            Name = "Play",
            Kind = LibraryKind.External,
            ContentType = ContentType.Mixed,
            RootPaths = [MediaPath],
        };
        db.Libraries.Add(library);

        var alien = new Title { Id = Guid.CreateVersion7(), Kind = TitleKind.Movie, Name = "Alien", Year = 1979 };
        var alienPlayable = Playable.ForMovie(Guid.CreateVersion7(), alien.Id);
        db.Titles.Add(alien);
        db.Playables.Add(alienPlayable);
        AddFile("Alien.1979.720p.mkv", 720, MediaFileStatus.Matched, alienPlayable.Id);
        AddFile("Alien.1979.2160p.mkv", 2160, MediaFileStatus.Missing, alienPlayable.Id);
        _alienBestFileId = AddFile("Alien.1979.1080p.mkv", 1080, MediaFileStatus.Matched, alienPlayable.Id).Id;
        _alienPlayableId = alienPlayable.Id;

        var ghost = new Title { Id = Guid.CreateVersion7(), Kind = TitleKind.Movie, Name = "Ghost", Year = 1990 };
        var ghostPlayable = Playable.ForMovie(Guid.CreateVersion7(), ghost.Id);
        db.Titles.Add(ghost);
        db.Playables.Add(ghostPlayable);
        AddFile("Ghost.1990.mkv", 1080, MediaFileStatus.Missing, ghostPlayable.Id);
        _ghostPlayableId = ghostPlayable.Id;

        var office = new Title { Id = Guid.CreateVersion7(), Kind = TitleKind.Show, Name = "The Office", Year = 2005 };
        var season = new Season { Id = Guid.CreateVersion7(), TitleId = office.Id, Number = 1 };
        var e1 = new Episode { Id = Guid.CreateVersion7(), SeasonId = season.Id, Number = 1, Name = "Episode 1" };
        var e2 = new Episode { Id = Guid.CreateVersion7(), SeasonId = season.Id, Number = 2, Name = "Episode 2" };
        var p1 = Playable.ForEpisode(Guid.CreateVersion7(), e1.Id);
        var p2 = Playable.ForEpisode(Guid.CreateVersion7(), e2.Id);
        db.Titles.Add(office);
        db.Seasons.Add(season);
        db.Episodes.AddRange(e1, e2);
        db.Playables.AddRange(p1, p2);
        var both = AddFile("The.Office.S01E01-E02.mkv", 720, MediaFileStatus.Matched, p1.Id);
        db.FileLinks.Add(new FileLink { MediaFileId = both.Id, PlayableId = p2.Id, Part = 2 });
        _episodeTwoPlayableId = p2.Id;

        await db.SaveChangesAsync(Ct);

        MediaFile AddFile(string name, int height, MediaFileStatus status, Guid playableId)
        {
            var path = Path.Combine(MediaPath, name);
            if (status != MediaFileStatus.Missing)
            {
                File.WriteAllBytes(path, []);
            }

            var file = new MediaFile
            {
                Id = Guid.CreateVersion7(),
                LibraryId = library.Id,
                Path = path,
                SizeBytes = 1_000,
                Mtime = DateTimeOffset.UnixEpoch,
                Probe = new ProbeResult
                {
                    Container = "matroska",
                    Streams = [new StreamInfo { Type = MediaStreamType.Video, Codec = "h264", Width = height * 16 / 9, Height = height }],
                },
                Status = status,
                MissingSince = status == MediaFileStatus.Missing ? DateTimeOffset.UnixEpoch : null,
            };
            db.MediaFiles.Add(file);
            db.FileLinks.Add(new FileLink { MediaFileId = file.Id, PlayableId = playableId, Part = 1 });
            return file;
        }
    }
}
