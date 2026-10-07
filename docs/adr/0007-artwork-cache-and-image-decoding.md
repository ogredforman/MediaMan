# 0007. Artwork cache, image fetching and decoding

- **Status:** Accepted
- **Date:** 2026-10-07
- **PLAN task:** 2.2

## Context

Artwork is cached locally and each image's dominant color tints the detail hero (SPEC §8 metadata [M], §11).
That needs two things the server didn't have: a way to fetch images by URL, which is an external dependency and
so must sit behind a SPEC §10.3 interface, and a way to decode images to pixels on every platform (SPEC §9).

## Options considered

Fetching:

1. **New `IImageDownloader`** (`Task<byte[]> DownloadAsync(Uri url, CancellationToken ct)`), real over HttpClient,
   faked with generated images. Any provider's artwork URL goes through it, including fanart.tv later.
2. **A method on `IMetadataProvider`.** Ties image fetching to one provider and makes every provider fake serve bytes.

Decoding:

1. **StbImageSharp** (public domain / MIT): managed C# port of stb_image, no native binaries. JPEG, PNG, BMP,
   GIF, TGA, PSD, HDR. No SVG, WebP or AVIF.
2. **ImageSharp**: managed and broad, but under the Six Labors Split License rather than a plain OSI licence.
3. **SkiaSharp** (MIT): broad, but ships a native library per platform and is far larger than needed.
4. **ffmpeg as a process**: already bundled, decodes everything, but a process per image is heavy for a scan.

## Decision

`IImageDownloader` (option 1) and StbImageSharp. TMDB serves posters, backdrops and stills as JPEG and logos as
PNG or SVG; only posters and backdrops are used for tinting, so an SVG logo is cached and served without a color.
If WebP or AVIF artwork ever matters (fanart.tv is PNG/JPG), decode it through the bundled ffmpeg as a separate
process (SPEC §10.1) rather than adding a native image library.

The cache lives in `<DataDirectory>/cache/artwork`. Files are named `<artworkId>.<ext>` with the extension
from the sniffed format (never the URL), and `Artwork.LocalPath` holds the bare file name so a copied data
directory still works. Images are served from `GET /artwork/{artworkId}` with `nosniff` and a locking-down
Content-Security-Policy, because an SVG can carry script. `ArtworkRef.url` is that path, and clients load it
as-is, so PLAN 2.3 can add a signature for `<img>` requests, which can't send a bearer token.

## Consequences

- Scans gain an `artwork` phase after importing; failed fetches are logged and retried on the next scan or request.
- The cache refuses to write when its folder is inside an External library root (SPEC §5).
- Cached files of deleted Artwork rows aren't pruned yet (Parked).
