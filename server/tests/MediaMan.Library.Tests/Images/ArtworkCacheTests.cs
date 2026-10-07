using MediaMan.Core.Domain;
using MediaMan.Fakes;
using MediaMan.Library.Images;
using Microsoft.EntityFrameworkCore;

namespace MediaMan.Library.Tests.Images;

public sealed class ArtworkCacheTests : IAsyncLifetime
{
    private readonly FakeImageDownloader _images = new();
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mediaman-tests", "artwork-" + Guid.NewGuid().ToString("N"));
    private readonly Guid _titleId = Guid.CreateVersion7();
    private TempDatabase _db = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string CacheDirectory => Path.Combine(_root, "data", "cache", "artwork");

    public async ValueTask InitializeAsync()
    {
        _db = await TempDatabase.CreateMigratedAsync(Ct);
        await using var context = _db.CreateContext();
        context.Titles.Add(new Title { Id = _titleId, Kind = TitleKind.Movie, Name = "Alien" });
        await context.SaveChangesAsync(Ct);
    }

    public async ValueTask DisposeAsync()
    {
        await _db.DisposeAsync();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task Uncached_artwork_is_cached_with_its_color_and_a_failure_doesnt_stop_the_rest()
    {
        _images.Serve("backdrop", TestImages.Solid("#204060")).Fail("broken");
        var poster = await AddArtworkAsync(ArtworkType.Poster, "https://img.example/poster.jpg");
        var broken = await AddArtworkAsync(ArtworkType.Logo, "https://img.example/broken.png");
        var backdrop = await AddArtworkAsync(ArtworkType.Backdrop, "https://img.example/backdrop.jpg");

        var result = await CacheMissingAsync();

        result.Should().Be(new ArtworkCacheResult(2, 1));
        await using var context = _db.CreateContext();
        var rows = await context.Artwork.ToDictionaryAsync(a => a.Id, Ct);
        rows[poster].DominantColor.Should().Be(FakeImageDownloader.DefaultColor);
        rows[backdrop].DominantColor.Should().Be("#204060");
        rows[broken].LocalPath.Should().BeNull();
        rows[poster].LocalPath.Should().Be(poster.ToString("N") + ".bmp", "it's a bare file name, so a copied data directory still works");
        Directory.GetFiles(CacheDirectory).Select(Path.GetFileName).Should().BeEquivalentTo(rows[poster].LocalPath, rows[backdrop].LocalPath);
    }

    [Fact]
    public async Task Cached_artwork_is_not_fetched_again_and_a_failure_is_retried_next_run()
    {
        _images.Fail("poster");
        await AddArtworkAsync(ArtworkType.Poster, "https://img.example/poster.jpg");
        (await CacheMissingAsync()).Should().Be(new ArtworkCacheResult(0, 1));

        _images.Serve("poster", TestImages.Solid("#102030"));
        (await CacheMissingAsync()).Should().Be(new ArtworkCacheResult(1, 0));
        (await CacheMissingAsync()).Should().Be(new ArtworkCacheResult(0, 0));

        _images.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task Lookup_fetches_on_demand_and_reports_unknown_and_unavailable_images()
    {
        _images.Fail("broken");
        var poster = await AddArtworkAsync(ArtworkType.Poster, "https://img.example/poster.jpg");
        var broken = await AddArtworkAsync(ArtworkType.Logo, "https://img.example/broken.png");
        await using var context = _db.CreateContext();
        var cache = new ArtworkCache(context, _images, new ArtworkCacheOptions { Directory = CacheDirectory });

        var found = await cache.GetAsync(poster, Ct);

        found.Status.Should().Be(ArtworkLookupStatus.Found);
        found.ContentType.Should().Be("image/bmp");
        Path.GetDirectoryName(found.FilePath).Should().Be(Path.GetFullPath(CacheDirectory));
        (await cache.GetAsync(broken, Ct)).Status.Should().Be(ArtworkLookupStatus.Unavailable);
        (await cache.GetAsync(Guid.NewGuid(), Ct)).Status.Should().Be(ArtworkLookupStatus.NotFound);
    }

    [Fact]
    public async Task The_cache_refuses_to_write_inside_an_external_library()
    {
        // SPEC §5: nothing is ever written under an External library root, even if the data directory is put there.
        var media = Path.Combine(_root, "media");
        await using (var context = _db.CreateContext())
        {
            context.Libraries.Add(new Core.Domain.Library
            {
                Id = Guid.CreateVersion7(),
                Name = "Media",
                Kind = LibraryKind.External,
                ContentType = ContentType.Mixed,
                RootPaths = [media + Path.DirectorySeparatorChar],
            });
            await context.SaveChangesAsync(Ct);
        }

        await AddArtworkAsync(ArtworkType.Poster, "https://img.example/poster.jpg");

        var act = () => CacheMissingAsync(Path.Combine(media, "MediaMan", "cache", "artwork"));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*inside an External library*");
        Directory.Exists(media).Should().BeFalse();
        _images.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task A_run_gives_up_after_too_many_failures_in_a_row()
    {
        // An outage shouldn't hold the scan for a timeout per image; the rest are tried next run.
        _images.Fail("img.example");
        var count = ArtworkCache.ConsecutiveFailureLimit + 2;
        for (var i = 0; i < count; i++)
        {
            await AddArtworkAsync(ArtworkType.Poster, $"https://img.example/{i}.jpg");
        }

        (await CacheMissingAsync()).Should().Be(new ArtworkCacheResult(0, count));
        _images.Requests.Should().HaveCount(ArtworkCache.ConsecutiveFailureLimit);
    }

    [Fact]
    public async Task A_cache_folder_that_cant_be_written_fails_each_image_without_throwing()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(CacheDirectory)!);
        await File.WriteAllTextAsync(CacheDirectory, "a file where the folder should be", Ct);
        var poster = await AddArtworkAsync(ArtworkType.Poster, "https://img.example/poster.jpg");

        (await CacheMissingAsync()).Should().Be(new ArtworkCacheResult(0, 1));
        await using var context = _db.CreateContext();
        (await context.Artwork.SingleAsync(a => a.Id == poster, Ct)).LocalPath.Should().BeNull();
    }

    private async Task<ArtworkCacheResult> CacheMissingAsync(string? directory = null)
    {
        await using var context = _db.CreateContext();
        var cache = new ArtworkCache(context, _images, new ArtworkCacheOptions { Directory = directory ?? CacheDirectory });
        return await cache.CacheMissingAsync(Ct);
    }

    private async Task<Guid> AddArtworkAsync(ArtworkType type, string url)
    {
        await using var context = _db.CreateContext();
        var id = Guid.CreateVersion7();
        context.Artwork.Add(new Artwork { Id = id, TitleId = _titleId, Type = type, Url = url });
        await context.SaveChangesAsync(Ct);
        return id;
    }
}
