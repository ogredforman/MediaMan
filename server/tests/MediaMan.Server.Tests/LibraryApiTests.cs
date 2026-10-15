using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MediaMan.Core.Domain;
using MediaMan.Data;
using MediaMan.Server.LibraryApi;
using MediaMan.Server.Tests.Conformance;
using Microsoft.Extensions.DependencyInjection;

namespace MediaMan.Server.Tests;

/// <summary>The library browse endpoints against a seeded database; every response is checked against the contract.</summary>
public sealed class LibraryApiTests : IAsyncLifetime
{
    private readonly MediaManFactory _factory = new();
    private HttpClient _client = null!;
    private Seed _seed = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _client = await _factory.CreateSignedInClientAsync(Ct); // starts the host, which migrates the database
        await using var scope = _factory.Services.CreateAsyncScope();
        _seed = await Seed.CreateAsync(scope.ServiceProvider.GetRequiredService<MediaManDbContext>(), Ct);
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task Libraries_are_listed_by_name_and_paged()
    {
        var first = await GetAsync<LibraryPage>("/libraries?limit=1");
        var second = await GetAsync<LibraryPage>($"/libraries?limit=1&cursor={first.PageInfo.NextCursor}");

        first.Items.Should().ContainSingle().Which.Name.Should().Be("Films");
        first.Items[0].Kind.Should().Be("external");
        first.Items[0].ContentType.Should().Be("movies");
        first.PageInfo.NextCursor.Should().NotBeNull();
        second.Items.Should().ContainSingle().Which.Name.Should().Be("TV");
        second.PageInfo.NextCursor.Should().BeNull();
    }

    [Fact]
    public async Task Titles_with_a_matched_file_are_listed_by_name_case_insensitively_across_pages()
    {
        var names = new List<string>();
        string? cursor = null;
        var pages = 0;
        do
        {
            var page = await GetAsync<TitlePage>("/titles?limit=2" + (cursor is null ? "" : $"&cursor={cursor}"));
            names.AddRange(page.Items.Select(t => t.Name));
            cursor = page.PageInfo.NextCursor;
            pages++;
        }
        while (cursor is not null);

        // "Gone Movie" has only a Missing file and "Orphan" has no files, so neither is listed.
        names.Should().Equal("alien", "Brick", "Casablanca", "Dune", "The Office");
        pages.Should().Be(3);
    }

    [Fact]
    public async Task Titles_carry_their_poster_and_summary_fields()
    {
        var page = await GetAsync<TitlePage>("/titles?kind=movie&limit=1");

        var alien = page.Items.Should().ContainSingle().Subject;
        alien.Kind.Should().Be("movie");
        alien.Year.Should().Be(1979);
        alien.Genres.Should().Equal("Horror", "Science Fiction");
        await AssertServesArtworkAsync(alien.Poster!.Url, "https://img.example/alien-poster.jpg");
        alien.Poster.DominantColor.Should().Be("#112233");
    }

    [Theory]
    [InlineData("kind=show", new[] { "The Office" })]
    [InlineData("kind=movie", new[] { "alien", "Brick", "Casablanca", "Dune" })]
    public async Task Titles_filter_by_kind(string query, string[] expected)
    {
        var page = await GetAsync<TitlePage>("/titles?" + query);

        page.Items.Select(t => t.Name).Should().Equal(expected);
    }

    [Fact]
    public async Task Titles_filter_by_library()
    {
        var tv = await GetAsync<TitlePage>($"/titles?libraryId={_seed.TvLibraryId}");
        var films = await GetAsync<TitlePage>($"/titles?libraryId={_seed.FilmsLibraryId}");

        tv.Items.Select(t => t.Name).Should().Equal("The Office");
        films.Items.Select(t => t.Name).Should().Equal("alien", "Brick", "Casablanca", "Dune");
    }

    [Theory]
    [InlineData("a", new[] { "alien", "Casablanca" })] // a name start ranks above a substring; Orphan has no files
    [InlineData("THE OFFICE", new[] { "The Office" })]
    [InlineData("off", new[] { "The Office" })]
    [InlineData("gone", new string[0])] // only a Missing file
    [InlineData("zzz", new string[0])]
    public async Task Search_ranks_titles_with_a_matched_file(string q, string[] expected)
    {
        var results = await GetAsync<LibrarySearchResults>($"/search?q={Uri.EscapeDataString(q)}");

        results.Query.Should().Be(q);
        results.Titles.Select(t => t.Name).Should().Equal(expected);
    }

    [Fact]
    public async Task Search_is_limited_and_carries_summaries_with_posters()
    {
        var results = await GetAsync<LibrarySearchResults>("/search?q=a&limit=1");

        var alien = results.Titles.Should().ContainSingle().Subject;
        alien.Id.Should().Be(_seed.AlienId);
        alien.Genres.Should().Equal("Horror", "Science Fiction");
        await AssertServesArtworkAsync(alien.Poster!.Url, "https://img.example/alien-poster.jpg");
    }

    [Fact]
    public async Task Blank_search_returns_no_titles()
    {
        var results = await GetAsync<LibrarySearchResults>("/search?q=%20%20");

        results.Query.Should().Be("  ");
        results.Titles.Should().BeEmpty();
    }

    [Fact]
    public async Task Search_longer_than_200_characters_is_a_400_problem_with_an_action()
    {
        using var response = await _client.GetAsync(new Uri("/search?q=" + new string('a', 201), UriKind.Relative), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await ContractDocument.Instance.AssertConformsAsync(response, Ct);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        body.RootElement.GetProperty("code").GetString().Should().Be("search.invalid");
        body.RootElement.GetProperty("action").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Movie_detail_lists_versions_best_first_with_missing_files_last()
    {
        var alien = await GetAsync<TitleDetail>($"/titles/{_seed.AlienId}");

        alien.Overview.Should().Be("In space, no one can hear you scream.");
        alien.RuntimeMinutes.Should().Be(117);
        alien.ExternalIds.Tmdb.Should().Be("movie/348");
        await AssertServesArtworkAsync(alien.Backdrop!.Url, "https://img.example/alien-backdrop.jpg");
        alien.Logo.Should().BeNull();
        alien.Seasons.Should().BeNull();
        alien.Playable!.Id.Should().Be(_seed.AlienPlayableId);
        alien.Playable.Versions.Select(v => v.FileName).Should().Equal(
            "Alien.1979.2160p.UHD.BluRay.x265-GRP.mkv", "Alien.1979.1080p.WEB-DL.mkv", "Alien.1979.720p.mkv");

        var uhd = alien.Playable.Versions[0];
        uhd.Status.Should().Be("matched");
        uhd.Part.Should().Be(1);
        uhd.Container.Should().Be("matroska");
        uhd.DurationSeconds.Should().Be(7020);
        uhd.Width.Should().Be(3840);
        uhd.Height.Should().Be(2160);
        uhd.VideoCodec.Should().Be("hevc");
        uhd.HdrFormat.Should().Be("HDR10");
        uhd.AudioCodec.Should().Be("truehd");
        uhd.AudioChannels.Should().Be(8);
        uhd.Source.Should().Be("BluRay");
        uhd.ReleaseGroup.Should().Be("GRP");
        alien.Playable.Versions[2].Status.Should().Be("missing");
    }

    [Fact]
    public async Task Show_detail_has_seasons_and_episodes_and_a_multi_episode_file_is_a_version_of_each()
    {
        var office = await GetAsync<TitleDetail>($"/titles/{_seed.OfficeId}");

        office.Playable.Should().BeNull();
        office.Seasons!.Select(s => s.Number).Should().Equal(0, 1);
        var season = office.Seasons![1];
        await AssertServesArtworkAsync(season.Poster!.Url, "https://img.example/office-s1.jpg");
        season.Episodes.Select(e => e.Number).Should().Equal(1, 2, 3);

        var (e1, e2, e3) = (season.Episodes[0], season.Episodes[1], season.Episodes[2]);
        await AssertServesArtworkAsync(e1.Still!.Url, "https://img.example/office-s1e1.jpg");
        e1.AirDate.Should().Be(new DateOnly(2005, 3, 24));
        e1.Versions.Should().ContainSingle().Which.MediaFileId.Should().Be(_seed.OfficeDoubleFileId);
        e2.Versions.Should().ContainSingle().Which.MediaFileId.Should().Be(_seed.OfficeDoubleFileId);
        e1.Versions[0].Part.Should().Be(1);
        e2.Versions[0].Part.Should().Be(2);
        e1.PlayableId.Should().NotBe(e2.PlayableId!.Value);

        // No file holds episode 3: it's listed, with no Playable and no versions.
        e3.PlayableId.Should().BeNull();
        e3.Versions.Should().BeEmpty();
    }

    [Fact]
    public async Task Absent_values_are_omitted_rather_than_null()
    {
        using var response = await _client.GetAsync(new Uri($"/titles/{_seed.OfficeId}", UriKind.Relative), Ct);
        var json = await response.Content.ReadAsStringAsync(Ct);

        json.Should().NotContain("null");
        await ContractDocument.Instance.AssertConformsAsync(response, Ct);
    }

    [Fact]
    public async Task Unknown_title_is_a_404_problem_with_an_action()
    {
        using var response = await _client.GetAsync(new Uri($"/titles/{Guid.NewGuid()}", UriKind.Relative), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        await ContractDocument.Instance.AssertConformsAsync(response, Ct);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        body.RootElement.GetProperty("code").GetString().Should().Be("title.not_found");
        body.RootElement.GetProperty("action").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData("genre=Drama", new[] { "Brick", "Casablanca" })] // "Gone Movie" and "Orphan" are Drama too, but not available
    [InlineData("genre=Drama&sort=rating", new[] { "Casablanca", "Brick" })]
    [InlineData("genre=drama", new string[0])] // matched exactly
    [InlineData("genre=", new[] { "alien", "Brick", "Casablanca", "Dune", "The Office" })]
    [InlineData("genre=%20", new[] { "alien", "Brick", "Casablanca", "Dune", "The Office" })]
    [InlineData("kind=show&genre=Comedy", new[] { "The Office" })]
    [InlineData("kind=movie&genre=Comedy", new string[0])]
    public async Task Titles_filter_by_genre(string query, string[] expected)
    {
        var page = await GetAsync<TitlePage>("/titles?" + query);

        page.Items.Select(t => t.Name).Should().Equal(expected);
    }

    [Fact]
    public async Task Titles_sort_by_rating_with_unrated_last_then_by_name_across_pages()
    {
        var first = await GetAsync<TitlePage>("/titles?sort=rating&limit=3");
        var second = await GetAsync<TitlePage>($"/titles?sort=rating&limit=3&cursor={first.PageInfo.NextCursor}");

        // The Office and Casablanca tie on rating, so they sort by name; Dune has no rating.
        first.Items.Select(t => t.Name).Should().Equal("Casablanca", "The Office", "alien");
        second.Items.Select(t => t.Name).Should().Equal("Brick", "Dune");
        second.PageInfo.NextCursor.Should().BeNull();
    }

    [Theory]
    [InlineData("", new[] { "Drama:2", "Science Fiction:2", "Comedy:1", "Horror:1", "Mystery:1", "Romance:1" })]
    [InlineData("?kind=movie", new[] { "Drama:2", "Science Fiction:2", "Horror:1", "Mystery:1", "Romance:1" })]
    [InlineData("?kind=show", new[] { "Comedy:1" })]
    public async Task Genres_count_available_titles_once_each_most_titles_first(string query, string[] expected)
    {
        var genres = await GetAsync<GenreList>("/genres" + query);

        genres.Items.Select(g => $"{g.Name}:{g.TitleCount}").Should().Equal(expected);
    }

    [Theory]
    [InlineData("/titles?cursor=not-a-cursor", "pagination.invalid")]
    [InlineData("/titles?cursor=eDox", "pagination.invalid")] // base64url of "x:1"
    [InlineData("/titles?limit=0", "pagination.invalid")]
    [InlineData("/libraries?limit=201", "pagination.invalid")]
    [InlineData("/titles?kind=Movie", "titles.invalid_kind")]
    [InlineData("/titles?sort=year", "titles.invalid_sort")]
    [InlineData("/genres?kind=films", "titles.invalid_kind")]
    [InlineData("/search", "search.invalid")]
    [InlineData("/search?q=a&limit=0", "search.invalid")]
    [InlineData("/search?q=a&limit=51", "search.invalid")]
    [InlineData("/search?q=a&limit=abc", "bad_request")] // a binding failure, the server-wide problem
    public async Task Invalid_list_requests_are_400_problems(string url, string code)
    {
        using var response = await _client.GetAsync(new Uri(url, UriKind.Relative), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await ContractDocument.Instance.AssertConformsAsync(response, Ct);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        body.RootElement.GetProperty("code").GetString().Should().Be(code);
    }

    /// <summary>The artwork URL is signed and served by this server, from the image the provider gave.</summary>
    private async Task AssertServesArtworkAsync(string url, string sourceUrl)
    {
        url.Should().MatchRegex(@"^/artwork/[0-9a-f-]{36}\?exp=[0-9]+&sig=[A-Za-z0-9_-]{43}$");
        using var response = await _client.GetAsync(new Uri(url, UriKind.Relative), Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _factory.Images.Requests.Should().Contain(new Uri(sourceUrl));
    }

    private async Task<T> GetAsync<T>(string url)
    {
        using var response = await _client.GetAsync(new Uri(url, UriKind.Relative), Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        await ContractDocument.Instance.AssertConformsAsync(response, Ct);
        return (await response.Content.ReadFromJsonAsync<T>(JsonSerializerOptions.Web, Ct))!;
    }

    /// <summary>
    /// Films: alien (3 versions, one Missing), Brick, Casablanca, Dune, Gone Movie (only a Missing file), Orphan (no files).
    /// Genres and ratings: alien Horror/Science Fiction 8.2, Casablanca Drama/Romance 8.6, Brick Drama/Mystery 7.3, Dune
    /// Science Fiction unrated, The Office Comedy 8.6; Gone Movie and Orphan are Drama.
    /// TV: The Office with specials, episodes 1–2 in one file and episode 3 without a file.
    /// </summary>
    private sealed class Seed
    {
        public Guid FilmsLibraryId { get; } = Guid.CreateVersion7();
        public Guid TvLibraryId { get; } = Guid.CreateVersion7();
        public Guid AlienId { get; private set; }
        public Guid AlienPlayableId { get; private set; }
        public Guid OfficeId { get; private set; }
        public Guid OfficeDoubleFileId { get; private set; }

        public static async Task<Seed> CreateAsync(MediaManDbContext db, CancellationToken ct)
        {
            var seed = new Seed();
            db.Libraries.AddRange(
                new Core.Domain.Library { Id = seed.TvLibraryId, Name = "TV", Kind = LibraryKind.External, ContentType = ContentType.Shows, RootPaths = [Root("tv")] },
                new Core.Domain.Library { Id = seed.FilmsLibraryId, Name = "Films", Kind = LibraryKind.External, ContentType = ContentType.Movies, RootPaths = [Root("films")] });

            var alien = seed.Movie(db, "alien", 1979, MediaFileStatus.Matched);
            seed.AlienId = alien.TitleId;
            seed.AlienPlayableId = alien.PlayableId;
            var title = db.Titles.Local.Single(t => t.Id == alien.TitleId);
            title.Overview = "In space, no one can hear you scream.";
            title.RuntimeMinutes = 117;
            title.Rating = 8.2;
            title.Genres = ["Horror", "Science Fiction"];
            title.ExternalIds.Tmdb = "movie/348";
            db.Artwork.AddRange(
                new Artwork { Id = Guid.CreateVersion7(), TitleId = alien.TitleId, Type = ArtworkType.Poster, Url = "https://img.example/alien-poster.jpg", DominantColor = "#112233" },
                new Artwork { Id = Guid.CreateVersion7(), TitleId = alien.TitleId, Type = ArtworkType.Backdrop, Url = "https://img.example/alien-backdrop.jpg" });
            db.MediaFiles.Local.Single(f => f.Id == alien.FileId).Path = Root("films", "Alien.1979.720p.mkv");
            db.MediaFiles.Local.Single(f => f.Id == alien.FileId).Status = MediaFileStatus.Missing;
            seed.AddVersion(db, alien.PlayableId, "Alien.1979.1080p.WEB-DL.mkv", Probe(1920, 1080, null), new ParsedRelease { Source = "WEB-DL" });
            seed.AddVersion(db, alien.PlayableId, "Alien.1979.2160p.UHD.BluRay.x265-GRP.mkv", Probe(3840, 2160, "HDR10"),
                new ParsedRelease { Source = "BluRay", Group = "GRP" });

            Describe(db, seed.Movie(db, "Casablanca", 1942, MediaFileStatus.Matched).TitleId, 8.6, "Drama", "Romance", "Drama");
            Describe(db, seed.Movie(db, "Brick", 2005, MediaFileStatus.Matched).TitleId, 7.3, "Drama", "Mystery");
            Describe(db, seed.Movie(db, "Dune", 2021, MediaFileStatus.Matched).TitleId, null, "Science Fiction");
            Describe(db, seed.Movie(db, "Gone Movie", 2000, MediaFileStatus.Missing).TitleId, 9.9, "Drama");
            db.Titles.Add(new Title { Id = Guid.CreateVersion7(), Kind = TitleKind.Movie, Name = "Orphan", Year = 2009, Genres = ["Drama"] });

            seed.AddOffice(db);
            Describe(db, seed.OfficeId, 8.6, "Comedy");
            await db.SaveChangesAsync(ct);
            return seed;
        }

        private static void Describe(MediaManDbContext db, Guid titleId, double? rating, params string[] genres)
        {
            var title = db.Titles.Local.Single(t => t.Id == titleId);
            title.Rating = rating;
            title.Genres = [.. genres];
        }

        private (Guid TitleId, Guid PlayableId, Guid FileId) Movie(MediaManDbContext db, string name, int year, MediaFileStatus status)
        {
            var title = new Title { Id = Guid.CreateVersion7(), Kind = TitleKind.Movie, Name = name, Year = year };
            var playable = Playable.ForMovie(Guid.CreateVersion7(), title.Id);
            db.Titles.Add(title);
            db.Playables.Add(playable);
            var file = AddFile(db, FilmsLibraryId, Root("films", $"{name}.{year}.mkv"), status, Probe(1920, 1080, null), null);
            db.FileLinks.Add(new FileLink { MediaFileId = file.Id, PlayableId = playable.Id, Part = 1 });
            return (title.Id, playable.Id, file.Id);
        }

        private void AddVersion(MediaManDbContext db, Guid playableId, string fileName, ProbeResult probe, ParsedRelease parsed)
        {
            var file = AddFile(db, FilmsLibraryId, Root("films", fileName), MediaFileStatus.Matched, probe, parsed);
            db.FileLinks.Add(new FileLink { MediaFileId = file.Id, PlayableId = playableId, Part = 1 });
        }

        private void AddOffice(MediaManDbContext db)
        {
            var office = new Title { Id = Guid.CreateVersion7(), Kind = TitleKind.Show, Name = "The Office", Year = 2005 };
            var specials = new Season { Id = Guid.CreateVersion7(), TitleId = office.Id, Number = 0, Name = "Specials" };
            var s1 = new Season { Id = Guid.CreateVersion7(), TitleId = office.Id, Number = 1, Name = "Season 1" };
            var episodes = Enumerable.Range(1, 3)
                .Select(n => new Episode { Id = Guid.CreateVersion7(), SeasonId = s1.Id, Number = n, Name = $"Episode {n}", AirDate = new DateOnly(2005, 3, 24).AddDays(7 * (n - 1)) })
                .Reverse() // stored out of order; the API sorts by number
                .ToList();
            db.Titles.Add(office);
            db.Seasons.AddRange(s1, specials);
            db.Episodes.AddRange(episodes);
            db.Episodes.Add(new Episode { Id = Guid.CreateVersion7(), SeasonId = specials.Id, Number = 1, Name = "Special" });

            var e1 = episodes.Single(e => e.Number == 1);
            var e2 = episodes.Single(e => e.Number == 2);
            db.Artwork.AddRange(
                new Artwork { Id = Guid.CreateVersion7(), SeasonId = s1.Id, Type = ArtworkType.Poster, Url = "https://img.example/office-s1.jpg" },
                new Artwork { Id = Guid.CreateVersion7(), EpisodeId = e1.Id, Type = ArtworkType.Still, Url = "https://img.example/office-s1e1.jpg" });

            var p1 = Playable.ForEpisode(Guid.CreateVersion7(), e1.Id);
            var p2 = Playable.ForEpisode(Guid.CreateVersion7(), e2.Id);
            db.Playables.AddRange(p1, p2);
            var file = AddFile(db, TvLibraryId, Root("tv", "The.Office.S01E01-E02.mkv"), MediaFileStatus.Matched, Probe(1280, 720, null), null);
            db.FileLinks.AddRange(
                new FileLink { MediaFileId = file.Id, PlayableId = p1.Id, Part = 1 },
                new FileLink { MediaFileId = file.Id, PlayableId = p2.Id, Part = 2 });
            OfficeId = office.Id;
            OfficeDoubleFileId = file.Id;
        }

        private static MediaFile AddFile(
            MediaManDbContext db, Guid libraryId, string path, MediaFileStatus status, ProbeResult probe, ParsedRelease? parsed)
        {
            var file = new MediaFile
            {
                Id = Guid.CreateVersion7(),
                LibraryId = libraryId,
                Path = path,
                SizeBytes = 1_000_000,
                Mtime = DateTimeOffset.UnixEpoch,
                Probe = probe,
                Parsed = parsed,
                Status = status,
                MissingSince = status == MediaFileStatus.Missing ? DateTimeOffset.UnixEpoch : null,
            };
            db.MediaFiles.Add(file);
            return file;
        }

        private static ProbeResult Probe(int width, int height, string? hdr) => new()
        {
            Container = "matroska",
            Duration = TimeSpan.FromSeconds(7020),
            HdrFormat = hdr,
            Streams =
            [
                new StreamInfo { Index = 0, Type = MediaStreamType.Video, Codec = height > 1080 ? "hevc" : "h264", Width = width, Height = height },
                new StreamInfo { Index = 1, Type = MediaStreamType.Audio, Codec = "ac3", Channels = 6 },
                new StreamInfo { Index = 2, Type = MediaStreamType.Audio, Codec = "truehd", Channels = 8, IsDefault = true },
                new StreamInfo { Index = 3, Type = MediaStreamType.Video, Codec = "mjpeg", Width = 600, Height = 900 }, // cover art
            ],
        };

        private static string Root(params string[] parts) => Path.Combine([Path.GetTempPath(), "mediaman-library-api", .. parts]);
    }
}
