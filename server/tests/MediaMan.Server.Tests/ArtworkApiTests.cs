using System.Net;
using MediaMan.Core.Domain;
using MediaMan.Data;
using MediaMan.Fakes;
using MediaMan.Server.LibraryApi;
using MediaMan.Server.Tests.Conformance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MediaMan.Server.Tests;

/// <summary><c>GET /artwork/{artworkId}</c>: cached images served from the data directory, fetched on first request.</summary>
public sealed class ArtworkApiTests : IAsyncLifetime
{
    private readonly MediaManFactory _factory = new();
    private HttpClient _client = null!;
    private Guid _titleId;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _client = _factory.CreateClient();
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MediaManDbContext>();
        _titleId = Guid.CreateVersion7();
        db.Titles.Add(new Title { Id = _titleId, Kind = TitleKind.Movie, Name = "Alien" });
        await db.SaveChangesAsync(Ct);
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task An_uncached_image_is_fetched_once_cached_with_its_color_and_served()
    {
        var image = TestImages.Solid("#336699");
        _factory.Images.Serve("alien-poster", image);
        var id = await AddArtworkAsync("https://img.example/alien-poster.jpg");

        using var first = await GetAsync(id);
        using var second = await GetAsync(id);

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        first.Content.Headers.ContentType!.MediaType.Should().Be("image/bmp");
        (await first.Content.ReadAsByteArrayAsync(Ct)).Should().Equal(image);
        first.Headers.CacheControl!.Private.Should().BeTrue();
        first.Headers.GetValues("X-Content-Type-Options").Should().Equal("nosniff");
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        _factory.Images.Requests.Should().ContainSingle();

        var stored = await ArtworkAsync(id);
        stored.DominantColor.Should().Be("#336699");
        File.Exists(Path.Combine(_factory.DataDirectory, "cache", "artwork", stored.LocalPath!)).Should().BeTrue();
    }

    [Fact]
    public async Task A_cached_file_that_went_missing_is_fetched_again()
    {
        var id = await AddArtworkAsync("https://img.example/alien-poster.jpg");
        (await GetAsync(id)).Dispose();
        File.Delete(Path.Combine(_factory.DataDirectory, "cache", "artwork", (await ArtworkAsync(id)).LocalPath!));

        using var response = await GetAsync(id);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _factory.Images.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task An_svg_is_served_so_nothing_in_it_can_run()
    {
        _factory.Images.Serve("logo", "<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>"u8.ToArray());
        var id = await AddArtworkAsync("https://img.example/logo.svg");

        using var response = await GetAsync(id);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("image/svg+xml");
        response.Headers.GetValues("Content-Security-Policy").Single().Should().Contain("default-src 'none'").And.Contain("sandbox");
        (await ArtworkAsync(id)).DominantColor.Should().BeNull();
    }

    [Fact]
    public async Task An_unknown_image_is_not_found()
    {
        using var response = await GetAsync(Guid.NewGuid());

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        await ContractDocument.Instance.AssertConformsAsync(response, Ct);
        (await response.Content.ReadAsStringAsync(Ct)).Should().Contain("artwork.not_found");
    }

    [Theory]
    [InlineData("https://img.example/broken.jpg")]
    [InlineData("file:///etc/passwd")]
    public async Task An_image_that_cant_be_fetched_is_unavailable_and_stays_uncached(string url)
    {
        _factory.Images.Fail("broken");
        var id = await AddArtworkAsync(url);

        using var response = await GetAsync(id);

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        await ContractDocument.Instance.AssertConformsAsync(response, Ct);
        (await response.Content.ReadAsStringAsync(Ct)).Should().Contain("artwork.unavailable");
        (await ArtworkAsync(id)).LocalPath.Should().BeNull();
    }

    [Fact]
    public async Task Data_that_isnt_an_image_is_not_cached()
    {
        _factory.Images.Serve("page", "<html>Not found</html>"u8.ToArray());
        var id = await AddArtworkAsync("https://img.example/page.jpg");

        using var response = await GetAsync(id);

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        (await ArtworkAsync(id)).LocalPath.Should().BeNull();
    }

    [Fact]
    public async Task Nothing_is_cached_when_the_data_directory_is_inside_an_external_library()
    {
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MediaManDbContext>();
            db.Libraries.Add(new Core.Domain.Library
            {
                Id = Guid.CreateVersion7(),
                Name = "Media",
                Kind = LibraryKind.External,
                ContentType = ContentType.Mixed,
                RootPaths = [_factory.DataDirectory],
            });
            await db.SaveChangesAsync(Ct);
        }

        var id = await AddArtworkAsync("https://img.example/alien-poster.jpg");

        using var response = await GetAsync(id);

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        await ContractDocument.Instance.AssertConformsAsync(response, Ct);
        (await response.Content.ReadAsStringAsync(Ct)).Should().Contain("artwork.cache_in_library");
        _factory.Images.Requests.Should().BeEmpty();
        Directory.Exists(Path.Combine(_factory.DataDirectory, "cache", "artwork")).Should().BeFalse();
    }

    private async Task<HttpResponseMessage> GetAsync(Guid artworkId) =>
        await _client.GetAsync(new Uri(ArtworkEndpoints.UrlOf(artworkId), UriKind.Relative), Ct);

    private async Task<Guid> AddArtworkAsync(string url)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MediaManDbContext>();
        var id = Guid.CreateVersion7();
        db.Artwork.Add(new Artwork { Id = id, TitleId = _titleId, Type = ArtworkType.Poster, Url = url });
        await db.SaveChangesAsync(Ct);
        return id;
    }

    private async Task<Artwork> ArtworkAsync(Guid id)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<MediaManDbContext>().Artwork.AsNoTracking().SingleAsync(a => a.Id == id, Ct);
    }
}
