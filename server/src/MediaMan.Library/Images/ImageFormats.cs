using System.Text;

namespace MediaMan.Library.Images;

/// <summary>An image format the artwork cache stores and serves.</summary>
public sealed record ImageFormat(string Extension, string ContentType);

/// <summary>Recognises image formats by their leading bytes, never by the URL, and maps cached file names back to a content type.</summary>
public static class ImageFormats
{
    public static readonly ImageFormat Jpeg = new(".jpg", "image/jpeg");
    public static readonly ImageFormat Png = new(".png", "image/png");
    public static readonly ImageFormat Gif = new(".gif", "image/gif");
    public static readonly ImageFormat Webp = new(".webp", "image/webp");
    public static readonly ImageFormat Bmp = new(".bmp", "image/bmp");
    public static readonly ImageFormat Svg = new(".svg", "image/svg+xml");

    private static readonly ImageFormat[] _all = [Jpeg, Png, Gif, Webp, Bmp, Svg];

    /// <summary>The format of <paramref name="data"/>, or null if it isn't an image the cache keeps.</summary>
    public static ImageFormat? Sniff(ReadOnlySpan<byte> data)
    {
        if (data.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF]))
        {
            return Jpeg;
        }

        if (data.StartsWith((ReadOnlySpan<byte>)[0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]))
        {
            return Png;
        }

        if (data.StartsWith("GIF87a"u8) || data.StartsWith("GIF89a"u8))
        {
            return Gif;
        }

        if (data.Length >= 12 && data.StartsWith("RIFF"u8) && data[8..12].SequenceEqual("WEBP"u8))
        {
            return Webp;
        }

        if (data.Length >= 26 && data.StartsWith("BM"u8))
        {
            return Bmp;
        }

        return IsSvg(data) ? Svg : null;
    }

    /// <summary>The content type of a cached file, by the extension <see cref="Sniff"/> gave it.</summary>
    public static string? ContentTypeOf(string fileName) =>
        Array.Find(_all, f => string.Equals(Path.GetExtension(fileName), f.Extension, StringComparison.OrdinalIgnoreCase))?.ContentType;

    private static bool IsSvg(ReadOnlySpan<byte> data)
    {
        var head = Encoding.UTF8.GetString(data[..Math.Min(data.Length, 1024)]).TrimStart('﻿', ' ', '\t', '\r', '\n');
        return (head.StartsWith("<svg", StringComparison.Ordinal) || head.StartsWith("<?xml", StringComparison.Ordinal))
            && head.Contains("<svg", StringComparison.Ordinal);
    }
}
