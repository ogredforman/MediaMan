namespace MediaMan.Playback.Streaming;

/// <summary>The content type a direct stream is served with, by file extension; the scanner's video extensions are all known.</summary>
public static class VideoContentTypes
{
    public const string Fallback = "application/octet-stream";

    private static readonly Dictionary<string, string> _types = new(StringComparer.OrdinalIgnoreCase)
    {
        [".mkv"] = "video/x-matroska",
        [".mp4"] = "video/mp4",
        [".m4v"] = "video/x-m4v",
        [".avi"] = "video/x-msvideo",
        [".mov"] = "video/quicktime",
        [".wmv"] = "video/x-ms-wmv",
        [".ts"] = "video/mp2t",
        [".m2ts"] = "video/mp2t",
        [".mts"] = "video/mp2t",
        [".webm"] = "video/webm",
        [".mpg"] = "video/mpeg",
        [".mpeg"] = "video/mpeg",
        [".vob"] = "video/mpeg",
        [".ogv"] = "video/ogg",
        [".flv"] = "video/x-flv",
    };

    public static string Of(string path) => _types.TryGetValue(Path.GetExtension(path), out var type) ? type : Fallback;
}
