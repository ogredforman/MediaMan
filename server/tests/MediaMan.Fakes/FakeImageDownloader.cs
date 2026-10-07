using System.Buffers.Binary;
using System.Globalization;
using MediaMan.Core.Modules;

namespace MediaMan.Fakes;

/// <summary>
/// Serves generated images with no network (SPEC §10.4): every URL gets a solid <see cref="DefaultColor"/> BMP unless a
/// test serves other bytes or makes it fail. Rules match when the URL contains their text; the latest rule wins.
/// </summary>
public sealed class FakeImageDownloader : IImageDownloader
{
    public const string DefaultColor = "#6A3FB5";

    private readonly Lock _gate = new();
    private readonly List<(string UrlPart, byte[]? Image)> _rules = [];
    private readonly List<Uri> _requests = [];

    public IReadOnlyList<Uri> Requests
    {
        get
        {
            lock (_gate)
            {
                return [.. _requests];
            }
        }
    }

    /// <summary>Serves <paramref name="image"/> for URLs containing <paramref name="urlPart"/>.</summary>
    public FakeImageDownloader Serve(string urlPart, byte[] image)
    {
        lock (_gate)
        {
            _rules.Add((urlPart, image));
        }

        return this;
    }

    /// <summary>Fails URLs containing <paramref name="urlPart"/> with <see cref="HttpRequestException"/>.</summary>
    public FakeImageDownloader Fail(string urlPart)
    {
        lock (_gate)
        {
            _rules.Add((urlPart, null));
        }

        return this;
    }

    public Task<byte[]> DownloadAsync(Uri url, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(url);
        ct.ThrowIfCancellationRequested();
        lock (_gate)
        {
            _requests.Add(url);
            var rule = _rules.LastOrDefault(r => url.AbsoluteUri.Contains(r.UrlPart, StringComparison.Ordinal));
            if (rule.UrlPart is null)
            {
                return Task.FromResult(TestImages.Solid(DefaultColor));
            }

            return rule.Image is { } image
                ? Task.FromResult(image)
                : Task.FromException<byte[]>(new HttpRequestException($"Fake failure fetching {url}."));
        }
    }
}

/// <summary>Uncompressed 24-bit BMPs built in memory, decodable by the artwork cache without any image files.</summary>
public static class TestImages
{
    public static byte[] Solid(string hex, int width = 4, int height = 4)
    {
        ArgumentNullException.ThrowIfNull(hex);
        var rgb = int.Parse(hex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var color = ((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        return Bmp(width, height, (_, _) => color);
    }

    /// <summary>A BMP whose pixel at column x, row y (from the top) is <paramref name="pixel"/>(x, y).</summary>
    public static byte[] Bmp(int width, int height, Func<int, int, (byte R, byte G, byte B)> pixel)
    {
        ArgumentNullException.ThrowIfNull(pixel);
        var stride = (width * 3 + 3) & ~3;
        var bytes = new byte[54 + stride * height];
        var span = bytes.AsSpan();
        "BM"u8.CopyTo(span);
        BinaryPrimitives.WriteInt32LittleEndian(span[2..], bytes.Length);
        BinaryPrimitives.WriteInt32LittleEndian(span[10..], 54);
        BinaryPrimitives.WriteInt32LittleEndian(span[14..], 40);
        BinaryPrimitives.WriteInt32LittleEndian(span[18..], width);
        BinaryPrimitives.WriteInt32LittleEndian(span[22..], height);
        BinaryPrimitives.WriteInt16LittleEndian(span[26..], 1);
        BinaryPrimitives.WriteInt16LittleEndian(span[28..], 24);
        for (var y = 0; y < height; y++)
        {
            var row = 54 + (height - 1 - y) * stride; // rows are stored bottom-up
            for (var x = 0; x < width; x++)
            {
                var (r, g, b) = pixel(x, y);
                bytes[row + x * 3] = b;
                bytes[row + x * 3 + 1] = g;
                bytes[row + x * 3 + 2] = r;
            }
        }

        return bytes;
    }
}
