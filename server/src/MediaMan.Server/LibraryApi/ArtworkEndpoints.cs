using MediaMan.Auth;
using MediaMan.Core;
using MediaMan.Library.Images;
using MediaMan.Server.Endpoints;
using MediaMan.Server.Hosting;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MediaMan.Server.LibraryApi;

/// <summary>Serves cached artwork images, fetching any that aren't cached yet (SPEC §8 metadata [M]).</summary>
public static class ArtworkEndpoints
{
    /// <summary>The path of an image; an <c>ArtworkRef</c> carries it signed by <see cref="SignedUrlOf"/>.</summary>
    public static string UrlOf(Guid artworkId) => $"/artwork/{artworkId}";

    /// <summary>The URL an <c>ArtworkRef</c> carries: signed, so <c>&lt;img&gt;</c> loads it without a token (ADR 0008).</summary>
    public static string SignedUrlOf(this UrlSigner signer, Guid artworkId) => signer.Sign(UrlOf(artworkId));

    public static IEndpointRouteBuilder MapArtworkEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/artwork/{artworkId}", GetArtworkAsync).RequiresPermission(Permissions.LibraryView).AllowsSignedUrl();
        return app;
    }

    private static async Task<Results<PhysicalFileHttpResult, ProblemHttpResult>> GetArtworkAsync(
        Guid artworkId, ArtworkCache cache, HttpResponse response, CancellationToken ct)
    {
        var lookup = await cache.GetAsync(artworkId, ct);
        switch (lookup.Status)
        {
            case ArtworkLookupStatus.NotFound:
                return Problems.Result(StatusCodes.Status404NotFound, "artwork.not_found", "Image not found.",
                    "This image doesn't exist. The title may have been rematched or removed.", "Reload the page to get its current artwork.");
            case ArtworkLookupStatus.Unavailable:
                return Problems.Result(StatusCodes.Status502BadGateway, "artwork.unavailable", "Image unavailable.",
                    "The image couldn't be fetched from the metadata provider.",
                    "Check the server's internet connection. The image is fetched again on the next scan or when you reload.");
            case ArtworkLookupStatus.CacheInsideLibrary:
                return Problems.Result(StatusCodes.Status500InternalServerError, "artwork.cache_in_library", "Artwork can't be cached.",
                    "The server's data directory is inside an External library, and MediaMan never writes to External libraries.",
                    "Move the server's data directory out of your media folders, then restart the server.");
        }

        // An id's image never changes. SVG logos can carry script, so nothing in an image may run or be sniffed as HTML.
        response.Headers.CacheControl = "private, max-age=31536000, immutable";
        response.Headers.ContentSecurityPolicy = "default-src 'none'; style-src 'unsafe-inline'; sandbox";
        response.Headers.XContentTypeOptions = "nosniff";
        return TypedResults.PhysicalFile(lookup.FilePath!, lookup.ContentType);
    }
}
