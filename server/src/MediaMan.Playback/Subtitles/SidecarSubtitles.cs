using System.Security.Cryptography;
using System.Text;

namespace MediaMan.Playback.Subtitles;

/// <summary>
/// An external subtitle file next to a video. <see cref="Id"/> is stable for its file name and safe in a URL, so a
/// subtitle is served by id and never by a path a client sent.
/// </summary>
public sealed record SidecarSubtitle(string Id, string Path, string Format, string? Language, bool Forced)
{
    public string FileName => System.IO.Path.GetFileName(Path);

    /// <summary>The response content type for <see cref="Format"/>.</summary>
    public string ContentType => Format switch
    {
        "srt" => "application/x-subrip",
        "vtt" => "text/vtt",
        _ => "text/x-ssa",
    };
}

/// <summary>
/// Finds the external subtitles of a video (SPEC §7.4 step 2): files in the same folder named after it, such as
/// <c>Alien.srt</c>, <c>Alien.en.srt</c> or <c>Alien.eng.forced.ass</c>. Only reads the folder; nothing is written.
/// </summary>
public static class SidecarSubtitles
{
    private static readonly Dictionary<string, string> _formats = new(StringComparer.OrdinalIgnoreCase)
    {
        [".srt"] = "srt",
        [".vtt"] = "vtt",
        [".ass"] = "ass",
        [".ssa"] = "ssa",
    };

    /// <summary>The video's sidecar subtitles, ordered by file name; empty when its folder can't be read.</summary>
    public static IReadOnlyList<SidecarSubtitle> Find(string videoPath)
    {
        var folder = Path.GetDirectoryName(videoPath);
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            return [];
        }

        try
        {
            return [.. Directory.EnumerateFiles(folder)
                .Select(candidate => Parse(videoPath, candidate))
                .OfType<SidecarSubtitle>()
                .OrderBy(s => s.FileName, StringComparer.Ordinal)];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>
    /// The subtitle <paramref name="candidatePath"/> is for <paramref name="videoPath"/>, or null if it isn't one:
    /// a subtitle extension, and the video's name followed by nothing or by dot-separated tags.
    /// </summary>
    public static SidecarSubtitle? Parse(string videoPath, string candidatePath)
    {
        if (!_formats.TryGetValue(Path.GetExtension(candidatePath), out var format))
        {
            return null;
        }

        var stem = Path.GetFileNameWithoutExtension(videoPath);
        var name = Path.GetFileNameWithoutExtension(candidatePath);
        if (stem.Length == 0 || !name.StartsWith(stem, StringComparison.OrdinalIgnoreCase)
            || (name.Length > stem.Length && name[stem.Length] != '.'))
        {
            return null;
        }

        string? language = null;
        var forced = false;
        foreach (var tag in name[stem.Length..].Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (tag.Equals("forced", StringComparison.OrdinalIgnoreCase))
            {
                forced = true;
            }
            else if (language is null && IsLanguageTag(tag))
            {
                language = tag;
            }
        }

        return new SidecarSubtitle(IdOf(Path.GetFileName(candidatePath)), candidatePath, format, language, forced);
    }

    /// <summary>A short hex id from the file name, so URLs carry no names or separators.</summary>
    public static string IdOf(string fileName) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(fileName)))[..16];

    // "en", "eng", "pt-BR", "zh-Hans": 2–3 letters, optionally a region or script.
    private static bool IsLanguageTag(string tag)
    {
        var parts = tag.Split('-');
        return parts[0].Length is 2 or 3 && parts[0].All(char.IsAsciiLetter)
            && parts.Length <= 2 && parts.Skip(1).All(p => p.Length is >= 2 and <= 4 && p.All(char.IsAsciiLetterOrDigit));
    }
}
