using System.Diagnostics;
using System.Net;
using MediaMan.Core.Domain;
using MediaMan.Data;
using Microsoft.Extensions.DependencyInjection;

namespace MediaMan.Server.Tests;

/// <summary>
/// SPEC §9: library requests answer in under 100 ms at p95 on a 20,000-file library. Search runs on every keystroke,
/// so it gets the check: 4,000 movies with two versions each and 400 shows of 30 episodes, then a spread of queries.
/// Explicit, so it runs on its own (`npm run test:perf`): wall-clock timing next to the rest of `npm test` measures CPU
/// contention, not the query.
/// </summary>
public sealed class SearchPerformanceTests : IAsyncLifetime
{
    private const int Movies = 4_000;
    private const int Shows = 400;
    private const int EpisodesPerShow = 30;

    private static readonly string[] _words =
    [
        "Amélie", "Night", "River", "Star", "Shadow", "Ghost", "Iron", "Silent", "Golden", "Dark", "Spider-Man",
        "Lost", "City", "Winter", "Summer", "Fire", "Ocean", "King's", "Garden", "Machine",
    ];

    private static readonly string[] _queries =
    [
        "a", "n", "st", "the", "riv", "shadow", "ghost ci", "amelie", "spider man", "kings", "winter fire",
        "golden garden 12", "machine 3999", "zzz", "o", "dark night", "lost", "iron", "summer 7", "ocean",
    ];

    private readonly MediaManFactory _factory = new();
    private HttpClient _client = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _client = await _factory.CreateSignedInClientAsync(Ct);
        await using var scope = _factory.Services.CreateAsyncScope();
        await SeedAsync(scope.ServiceProvider.GetRequiredService<MediaManDbContext>(), Ct);
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact(Explicit = true)]
    public async Task Search_answers_in_under_100_ms_at_p95_on_a_20000_file_library()
    {
        foreach (var q in _queries.Take(5))
        {
            await SearchAsync(q); // warm up the host, EF's query cache and SQLite's page cache
        }

        var timings = new List<double>();
        for (var round = 0; round < 3; round++)
        {
            foreach (var q in _queries)
            {
                var stopwatch = Stopwatch.StartNew();
                await SearchAsync(q);
                timings.Add(stopwatch.Elapsed.TotalMilliseconds);
            }
        }

        timings.Sort();
        var p95 = timings[(int)Math.Ceiling(timings.Count * 0.95) - 1];
        p95.Should().BeLessThan(100, $"p95 over {timings.Count} searches was {p95:F1} ms");
    }

    private async Task SearchAsync(string q)
    {
        using var response = await _client.GetAsync(new Uri($"/search?q={Uri.EscapeDataString(q)}", UriKind.Relative), Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await response.Content.ReadAsStringAsync(Ct);
    }

    private static string Name(int n) => $"{_words[n % _words.Length]} {_words[n / _words.Length % _words.Length]} {n}";

    private static async Task SeedAsync(MediaManDbContext db, CancellationToken ct)
    {
        db.ChangeTracker.AutoDetectChangesEnabled = false;
        var root = Path.Combine(Path.GetTempPath(), "mediaman-search-perf");
        var library = new Core.Domain.Library
        {
            Id = Guid.CreateVersion7(),
            Name = "Everything",
            Kind = LibraryKind.External,
            ContentType = ContentType.Mixed,
            RootPaths = [root],
        };
        db.Libraries.Add(library);
        var files = 0;

        MediaFile File(string name) => new()
        {
            Id = Guid.CreateVersion7(),
            LibraryId = library.Id,
            Path = Path.Combine(root, $"{name}.{files++}.mkv"),
            SizeBytes = 1_000_000,
            Mtime = DateTimeOffset.UnixEpoch,
            Status = MediaFileStatus.Matched,
        };

        for (var n = 0; n < Movies; n++)
        {
            var title = new Title { Id = Guid.CreateVersion7(), Kind = TitleKind.Movie, Name = Name(n), Year = 1950 + n % 75, Genres = ["Drama"] };
            var playable = Playable.ForMovie(Guid.CreateVersion7(), title.Id);
            db.Titles.Add(title);
            db.Playables.Add(playable);
            db.Artwork.Add(new Artwork { Id = Guid.CreateVersion7(), TitleId = title.Id, Type = ArtworkType.Poster, Url = $"https://img.example/{n}.jpg" });
            for (var version = 0; version < 2; version++)
            {
                var file = File(title.Name);
                db.MediaFiles.Add(file);
                db.FileLinks.Add(new FileLink { MediaFileId = file.Id, PlayableId = playable.Id, Part = 1 });
            }
        }

        for (var n = Movies; n < Movies + Shows; n++)
        {
            var title = new Title { Id = Guid.CreateVersion7(), Kind = TitleKind.Show, Name = Name(n), Year = 2000 + n % 25 };
            var season = new Season { Id = Guid.CreateVersion7(), TitleId = title.Id, Number = 1 };
            db.Titles.Add(title);
            db.Seasons.Add(season);
            for (var e = 1; e <= EpisodesPerShow; e++)
            {
                var episode = new Episode { Id = Guid.CreateVersion7(), SeasonId = season.Id, Number = e };
                var playable = Playable.ForEpisode(Guid.CreateVersion7(), episode.Id);
                var file = File(title.Name);
                db.Episodes.Add(episode);
                db.Playables.Add(playable);
                db.MediaFiles.Add(file);
                db.FileLinks.Add(new FileLink { MediaFileId = file.Id, PlayableId = playable.Id, Part = 1 });
            }
        }

        files.Should().Be(20_000);
        await db.SaveChangesAsync(ct);
    }
}
