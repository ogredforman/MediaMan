using MediaMan.Core.Domain;
using MediaMan.Core.Modules;
using MediaMan.Data;
using Microsoft.EntityFrameworkCore;

namespace MediaMan.Library.Images;

public sealed class ArtworkCacheOptions
{
    /// <summary>Where cached images live, normally <c>&lt;DataDirectory&gt;/cache/artwork</c> (SPEC §9 data portability).</summary>
    public required string Directory { get; init; }
}

/// <summary>Images cached by one <see cref="ArtworkCache.CacheMissingAsync"/> run, and the ones that couldn't be fetched.</summary>
public sealed record ArtworkCacheResult(int Cached, int Failed);

public enum ArtworkLookupStatus
{
    Found,
    NotFound,
    Unavailable,

    /// <summary>The cache folder is inside an External library root, so nothing is written (SPEC §5).</summary>
    CacheInsideLibrary,
}

/// <summary>A cached image ready to serve, or why there isn't one.</summary>
public sealed record ArtworkLookup(ArtworkLookupStatus Status, string? FilePath = null, string? ContentType = null);

/// <summary>
/// Caches artwork images locally and records each one's dominant color (SPEC §8 metadata [M], §11). Files are named
/// by Artwork id with the extension of their sniffed format, written whole through a temp file, and stored in
/// <see cref="Artwork.LocalPath"/> as a bare file name so a copied data directory still works. A failed fetch leaves the
/// row uncached for the next run or request; after <see cref="ConsecutiveFailureLimit"/> failures in a row a run gives
/// up, so an outage doesn't hold a scan for a timeout per image. The cache never writes inside an External library root
/// (SPEC §5).
/// </summary>
public sealed class ArtworkCache(MediaManDbContext db, IImageDownloader downloader, ArtworkCacheOptions options)
{
    public const int ConsecutiveFailureLimit = 5;

    /// <summary>Caches every Artwork row that isn't cached yet.</summary>
    public async Task<ArtworkCacheResult> CacheMissingAsync(CancellationToken ct)
    {
        var pending = await db.Artwork.Where(a => a.LocalPath == null).OrderBy(a => a.Id).ToListAsync(ct);
        if (pending.Count == 0)
        {
            return new ArtworkCacheResult(0, 0);
        }

        if (await IsInsideExternalRootAsync(ct))
        {
            throw new InvalidOperationException(
                $"The artwork cache '{options.Directory}' is inside an External library, which MediaMan never writes to. " +
                "Move the server's data directory out of your media folders.");
        }

        var cached = 0;
        var failedInARow = 0;
        foreach (var artwork in pending)
        {
            if (await TryCacheAsync(artwork, ct))
            {
                cached++;
                failedInARow = 0;
            }
            else if (++failedInARow == ConsecutiveFailureLimit)
            {
                break;
            }
        }

        return new ArtworkCacheResult(cached, pending.Count - cached);
    }

    /// <summary>The cached file for an Artwork row, fetching it first if it isn't cached or the file has gone.</summary>
    public async Task<ArtworkLookup> GetAsync(Guid artworkId, CancellationToken ct)
    {
        var artwork = await db.Artwork.SingleOrDefaultAsync(a => a.Id == artworkId, ct);
        if (artwork is null)
        {
            return new ArtworkLookup(ArtworkLookupStatus.NotFound);
        }

        if (Found(artwork) is { } found)
        {
            return found;
        }

        if (await IsInsideExternalRootAsync(ct))
        {
            return new ArtworkLookup(ArtworkLookupStatus.CacheInsideLibrary);
        }

        return await TryCacheAsync(artwork, ct) && Found(artwork) is { } fetched
            ? fetched
            : new ArtworkLookup(ArtworkLookupStatus.Unavailable);
    }

    private ArtworkLookup? Found(Artwork artwork)
    {
        if (artwork.LocalPath is not { } fileName || ImageFormats.ContentTypeOf(fileName) is not { } contentType)
        {
            return null;
        }

        var path = Path.GetFullPath(Path.Combine(options.Directory, Path.GetFileName(fileName)));
        return File.Exists(path) ? new ArtworkLookup(ArtworkLookupStatus.Found, path, contentType) : null;
    }

    private async Task<bool> TryCacheAsync(Artwork artwork, CancellationToken ct)
    {
        if (!Uri.TryCreate(artwork.Url, UriKind.Absolute, out var url) || (url.Scheme != Uri.UriSchemeHttps && url.Scheme != Uri.UriSchemeHttp))
        {
            return false;
        }

        byte[] data;
        try
        {
            data = await downloader.DownloadAsync(url, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidDataException)
        {
            return false;
        }

        if (ImageFormats.Sniff(data) is not { } format)
        {
            return false;
        }

        var fileName = artwork.Id.ToString("N") + format.Extension;
        var path = Path.Combine(options.Directory, fileName);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(options.Directory);
            await File.WriteAllBytesAsync(temp, data, ct);
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false; // e.g. a full disk: this image stays uncached and is retried like a failed fetch
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }

        artwork.LocalPath = fileName;
        artwork.DominantColor = DominantColor.FromImage(data);
        await db.SaveChangesAsync(ct);
        return true;
    }

    private async Task<bool> IsInsideExternalRootAsync(CancellationToken ct)
    {
        var roots = await db.Libraries.AsNoTracking()
            .Where(l => l.Kind == LibraryKind.External)
            .Select(l => l.RootPaths)
            .ToListAsync(ct);
        var cache = WithSeparator(options.Directory);
        var comparison = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        return roots.SelectMany(r => r).Any(root => cache.StartsWith(WithSeparator(root), comparison));
    }

    private static string WithSeparator(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)) + Path.DirectorySeparatorChar;
}
