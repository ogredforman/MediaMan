using System.Globalization;
using StbImageSharp;

namespace MediaMan.Library.Images;

/// <summary>
/// Picks an image's dominant color for hero tinting (SPEC §11): pixels are grouped into 4-bit-per-channel buckets and
/// the average of the fullest bucket wins. Transparent pixels never count, and near-black or near-white pixels (letterbox
/// bars, blank borders) count only when the image has little else. Pure: no I/O.
/// </summary>
public static class DominantColor
{
    private const long MaxPixels = 50_000_000;
    private const int MaxSamples = 16_384;
    private const int Buckets = 16 * 16 * 16;

    /// <summary>The dominant color as <c>#RRGGBB</c>, or null if the image can't be decoded (e.g. SVG) or is fully transparent.</summary>
    public static string? FromImage(byte[] image)
    {
        ArgumentNullException.ThrowIfNull(image);
        ImageResult decoded;
#pragma warning disable CA1031 // stb reports unknown and corrupt data with a bare Exception; either way there's no color.
        try
        {
            using var stream = new MemoryStream(image, writable: false);
            if (ImageInfo.FromStream(stream) is not { Width: > 0, Height: > 0 } info || (long)info.Width * info.Height > MaxPixels)
            {
                return null;
            }

            decoded = ImageResult.FromMemory(image, ColorComponents.RedGreenBlueAlpha);
        }
        catch (Exception)
        {
            return null;
        }
#pragma warning restore CA1031

        return FromPixels(decoded.Data);
    }

    /// <summary>The dominant color of RGBA pixels as <c>#RRGGBB</c>, or null if none is opaque.</summary>
    public static string? FromPixels(ReadOnlySpan<byte> rgba)
    {
        var pixels = rgba.Length / 4;
        var step = Math.Max(1, pixels / MaxSamples);
        var all = new Histogram();
        var colored = new Histogram();
        for (var p = 0; p < pixels; p += step)
        {
            var px = rgba.Slice(p * 4, 4);
            if (px[3] < 128)
            {
                continue;
            }

            all.Add(px[0], px[1], px[2]);
            var max = Math.Max(px[0], Math.Max(px[1], px[2]));
            var min = Math.Min(px[0], Math.Min(px[1], px[2]));
            if (max >= 24 && min <= 232)
            {
                colored.Add(px[0], px[1], px[2]);
            }
        }

        if (all.Total == 0)
        {
            return null;
        }

        return (colored.Total * 20 >= all.Total ? colored : all).Dominant();
    }

    private sealed class Histogram
    {
        private readonly int[] _count = new int[Buckets];
        private readonly long[] _r = new long[Buckets];
        private readonly long[] _g = new long[Buckets];
        private readonly long[] _b = new long[Buckets];

        public int Total { get; private set; }

        public void Add(byte r, byte g, byte b)
        {
            var bucket = (r >> 4 << 8) | (g >> 4 << 4) | (b >> 4);
            _count[bucket]++;
            _r[bucket] += r;
            _g[bucket] += g;
            _b[bucket] += b;
            Total++;
        }

        public string Dominant()
        {
            var best = 0;
            for (var i = 1; i < Buckets; i++)
            {
                if (_count[i] > _count[best])
                {
                    best = i;
                }
            }

            var n = _count[best];
            return string.Create(CultureInfo.InvariantCulture, $"#{_r[best] / n:X2}{_g[best] / n:X2}{_b[best] / n:X2}");
        }
    }
}
