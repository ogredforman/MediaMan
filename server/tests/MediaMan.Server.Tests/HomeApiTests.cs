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

/// <summary><c>GET /home</c>: the hero and the Continue Watching, Next Up, Recently Added and genre rows (SPEC §8 browsing).</summary>
public sealed class HomeApiTests : IAsyncLifetime
{
    private readonly FakeClock _clock = new();
    private readonly MediaManFactory _factory;
    private readonly string _media = Path.Combine(Path.GetTempPath(), "mediaman-tests", Guid.NewGuid().ToString("N"));
    private readonly Guid _libraryId = Guid.CreateVersion7();
    private readonly Dictionary<string, Guid> _titles = [];
    private readonly Dictionary<string, Guid> _playables = [];
    private HttpClient _client = null!;

    public HomeApiTests() =>
        _factory = new MediaManFactory { ConfigureServices = services => services.AddSingleton<IClock>(_clock) };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        // Five minutes before a UTC midnight, so the hero test reaches the next day while the access token lasts.
        _clock.Advance(_clock.UtcNow.UtcDateTime.Date.AddDays(1) - _clock.UtcNow.UtcDateTime - TimeSpan.FromMinutes(5));
        Directory.CreateDirectory(_media);
        var user = await _factory.CreateUserAsync(Ct, [Permissions.LibraryView]);
        _client = _factory.CreateClient(user);
        var profiles = await _client.GetFromJsonAsync<ProfileList>("/profiles", JsonSerializerOptions.Web, Ct);
        using (var picked = await _client.PutAsJsonAsync("/auth/session/profile", new SelectProfileRequest(profiles!.Items[0].Id), Ct))
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
    public async Task A_fresh_profile_sees_recently_added_and_genre_rows_and_a_hero()
    {
        var home = await HomeAsync(_client);

        home.Rows.Select(r => (r.Kind, r.Genre)).Should().Equal(
            (HomeRow.RecentlyAdded, null), (HomeRow.GenreKind, "Science Fiction"));
        home.Rows[0].Items.Select(i => i.Title.Name).Should().Equal("The Office", "Alien", "Aliens", "Arrival");
        home.Rows[0].Items.Should().AllSatisfy(i => (i.PlayableId, i.Episode, i.Progress).Should().Be(((Guid?)null, (HomeEpisode?)null, (HomeProgress?)null)));
        home.Rows[1].Items.Select(i => i.Title.Name).Should().Equal("Alien", "Aliens", "Arrival");
        home.Rows[0].Items[1].Title.Poster!.DominantColor.Should().Be("#112233");
    }

    [Fact]
    public async Task The_hero_is_a_recent_title_with_a_backdrop_and_changes_the_next_day()
    {
        var first = (await HomeAsync(_client)).Hero!;
        _clock.Advance(TimeSpan.FromMinutes(10));
        var second = (await HomeAsync(_client)).Hero!;

        new[] { first.Title.Name, second.Title.Name }.Should().BeEquivalentTo("The Office", "Alien");
        var office = first.Title.Name == "The Office" ? first : second;
        office.Overview.Should().Be("A mockumentary.");
        office.Backdrop.Should().NotBeNull();
        office.PlayableId.Should().Be(_playables["1x1"]);
        office.Episode.Should().BeEquivalentTo(new { SeasonNumber = 1, Number = 1, Name = "Pilot" });
        office.Episode!.Still.Should().NotBeNull();
        var alien = first.Title.Name == "Alien" ? first : second;
        (alien.PlayableId, alien.Episode, alien.ResumeSeconds).Should().Be(((Guid?)_playables["Alien"], (HomeEpisode?)null, (double?)null));
    }

    [Fact]
    public async Task Watching_fills_continue_watching_and_next_up_for_the_profile_only()
    {
        await ReportAsync("Alien", 1500, 6000);
        _clock.Advance(TimeSpan.FromMinutes(5));
        await ReportAsync("1x1", 1450, 1500);

        var home = await HomeAsync(_client);

        home.Rows.Select(r => r.Kind).Should().StartWith([HomeRow.ContinueWatching, HomeRow.NextUp, HomeRow.RecentlyAdded]);
        var resume = home.Rows[0].Items.Should().ContainSingle().Subject;
        (resume.Title.Name, resume.PlayableId, resume.Progress).Should().Be(("Alien", (Guid?)_playables["Alien"], new HomeProgress(1500, 6000)));
        var next = home.Rows[1].Items.Should().ContainSingle().Subject;
        (next.Title.Name, next.PlayableId, next.Progress).Should().Be(("The Office", (Guid?)_playables["1x2"], (HomeProgress?)null));
        next.Episode.Should().BeEquivalentTo(new { SeasonNumber = 1, Number = 2, Name = "Diversity Day" });

        var hero = home.Hero!;
        (hero.PlayableId, hero.ResumeSeconds).Should().Be(hero.Title.Name == "Alien"
            ? ((Guid?)_playables["Alien"], (double?)1500)
            : ((Guid?)_playables["1x2"], (double?)null));

        using var noProfile = await _factory.CreateSignedInClientAsync(Ct);
        (await HomeAsync(noProfile)).Rows.Select(r => r.Kind).Should().NotContain([HomeRow.ContinueWatching, HomeRow.NextUp]);
    }

    [Fact]
    public async Task An_empty_library_has_no_hero_and_no_rows()
    {
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MediaManDbContext>();
            db.MediaFiles.RemoveRange(db.MediaFiles);
            await db.SaveChangesAsync(Ct);
        }

        var home = await HomeAsync(_client);

        home.Hero.Should().BeNull();
        home.Rows.Should().BeEmpty();
    }

    private async Task ReportAsync(string playable, double position, double duration)
    {
        using var response = await _client.PutAsJsonAsync(
            $"/playables/{_playables[playable]}/progress", new { positionSeconds = position, durationSeconds = duration }, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
    }

    private static async Task<HomeDto> HomeAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/home", Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        await ContractDocument.Instance.AssertConformsAsync(response, Ct);
        return (await response.Content.ReadFromJsonAsync<HomeDto>(JsonSerializerOptions.Web, Ct))!;
    }

    /// <summary>
    /// Three science-fiction movies (Alien with a backdrop), a movie whose only file is missing, and The Office
    /// (S01E01–E02, with a backdrop) added last; each title's files are found a day apart.
    /// </summary>
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

        var day = 0;
        foreach (var (name, rating, genres, status) in new (string, double, string[], MediaFileStatus)[]
        {
            ("Arrival", 7.9, ["Science Fiction", "Drama"], MediaFileStatus.Matched),
            ("Gone", 9.9, ["Science Fiction"], MediaFileStatus.Missing),
            ("Aliens", 8.4, ["Science Fiction", "Action"], MediaFileStatus.Matched),
            ("Alien", 8.5, ["Science Fiction", "Horror"], MediaFileStatus.Matched),
        })
        {
            var movie = new Title { Id = Guid.CreateVersion7(), Kind = TitleKind.Movie, Name = name, Rating = rating, Genres = [.. genres] };
            var playable = Playable.ForMovie(Guid.CreateVersion7(), movie.Id);
            db.Titles.Add(movie);
            db.Playables.Add(playable);
            AddFile(db, playable.Id, $"{name}.mkv", status, day++);
            _titles[name] = movie.Id;
            _playables[name] = playable.Id;
        }

        db.Artwork.AddRange(
            new Artwork { Id = Guid.CreateVersion7(), TitleId = _titles["Alien"], Type = ArtworkType.Poster, Url = "https://img.example/alien-poster.jpg", DominantColor = "#112233" },
            new Artwork { Id = Guid.CreateVersion7(), TitleId = _titles["Alien"], Type = ArtworkType.Backdrop, Url = "https://img.example/alien-backdrop.jpg" },
            new Artwork { Id = Guid.CreateVersion7(), TitleId = _titles["Gone"], Type = ArtworkType.Backdrop, Url = "https://img.example/gone-backdrop.jpg" });

        var show = new Title { Id = Guid.CreateVersion7(), Kind = TitleKind.Show, Name = "The Office", Overview = "A mockumentary.", Genres = ["Comedy"] };
        var season = new Season { Id = Guid.CreateVersion7(), TitleId = show.Id, Number = 1 };
        db.Titles.Add(show);
        db.Seasons.Add(season);
        db.Artwork.Add(new Artwork { Id = Guid.CreateVersion7(), TitleId = show.Id, Type = ArtworkType.Backdrop, Url = "https://img.example/office-backdrop.jpg" });
        foreach (var (number, name) in new[] { (1, "Pilot"), (2, "Diversity Day") })
        {
            var episode = new Episode { Id = Guid.CreateVersion7(), SeasonId = season.Id, Number = number, Name = name };
            var playable = Playable.ForEpisode(Guid.CreateVersion7(), episode.Id);
            db.Episodes.Add(episode);
            db.Playables.Add(playable);
            db.Artwork.Add(new Artwork { Id = Guid.CreateVersion7(), EpisodeId = episode.Id, Type = ArtworkType.Still, Url = $"https://img.example/office-1x{number}.jpg" });
            AddFile(db, playable.Id, $"1x{number}.mkv", MediaFileStatus.Matched, day);
            _playables[$"1x{number}"] = playable.Id;
        }

        await db.SaveChangesAsync(Ct);
    }

    private void AddFile(MediaManDbContext db, Guid playableId, string name, MediaFileStatus status, int day)
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
            AddedAt = _clock.UtcNow.AddDays(day - 10),
            Status = status,
            MissingSince = status == MediaFileStatus.Missing ? DateTimeOffset.UnixEpoch : null,
        };
        db.MediaFiles.Add(file);
        db.FileLinks.Add(new FileLink { MediaFileId = file.Id, PlayableId = playableId, Part = 1 });
    }
}
