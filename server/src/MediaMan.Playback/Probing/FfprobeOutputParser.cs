using System.Globalization;
using System.Text.Json;
using MediaMan.Core.Domain;

namespace MediaMan.Playback.Probing;

/// <summary>
/// Turns ffprobe's <c>-print_format json -show_format -show_streams</c> output into a <see cref="ProbeResult"/>.
/// HDR names match the canonical values <c>ReleaseParser</c> uses, so a probe and a release name compare directly.
/// </summary>
public static class FfprobeOutputParser
{
    private const string DolbyVisionSideData = "DOVI configuration record";

    /// <exception cref="InvalidDataException">The JSON is malformed or has no format section.</exception>
    public static ProbeResult Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("format", out var format)
                || String(format, "format_name") is not { } container)
            {
                throw new InvalidDataException("ffprobe output has no container format.");
            }

            List<StreamInfo> streams = [];
            string? hdrFormat = null;
            if (root.TryGetProperty("streams", out var streamArray) && streamArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var stream in streamArray.EnumerateArray())
                {
                    if (ParseStream(stream) is not { } info)
                    {
                        continue;
                    }

                    streams.Add(info);
                    if (info.Type == MediaStreamType.Video)
                    {
                        hdrFormat ??= HdrFormat(stream);
                    }
                }
            }

            return new ProbeResult
            {
                Container = container,
                Duration = Double(format, "duration") is { } seconds && seconds > 0 ? TimeSpan.FromSeconds(seconds) : null,
                HdrFormat = hdrFormat,
                Streams = streams,
            };
        }
        catch (JsonException e)
        {
            throw new InvalidDataException("ffprobe output is not valid JSON.", e);
        }
    }

    private static StreamInfo? ParseStream(JsonElement stream)
    {
        JsonElement? disposition = stream.TryGetProperty("disposition", out var d) ? d : null;
        JsonElement? tags = stream.TryGetProperty("tags", out var t) && t.ValueKind == JsonValueKind.Object ? t : null;
        MediaStreamType? type = String(stream, "codec_type") switch
        {
            // Embedded cover art is reported as a one-frame video stream; it isn't something to play.
            "video" when Flag(disposition, "attached_pic") => MediaStreamType.Attachment,
            "video" => MediaStreamType.Video,
            "audio" => MediaStreamType.Audio,
            "subtitle" => MediaStreamType.Subtitle,
            "attachment" => MediaStreamType.Attachment,
            "data" => MediaStreamType.Data,
            _ => null,
        };
        if (type is null)
        {
            return null;
        }

        return new StreamInfo
        {
            Index = Int(stream, "index") ?? 0,
            Type = type.Value,
            Codec = String(stream, "codec_name") ?? "unknown",
            Language = Tag(tags, "language"),
            Title = Tag(tags, "title"),
            IsDefault = Flag(disposition, "default"),
            IsForced = Flag(disposition, "forced"),
            Width = Int(stream, "width"),
            Height = Int(stream, "height"),
            Channels = Int(stream, "channels"),
            BitRate = Long(stream, "bit_rate") ?? MatroskaBitRate(tags),
        };
    }

    // Dolby Vision files usually carry an HDR10 base layer, so the DV record wins over the transfer function.
    private static string? HdrFormat(JsonElement stream)
    {
        if (stream.TryGetProperty("side_data_list", out var sideData) && sideData.ValueKind == JsonValueKind.Array
            && sideData.EnumerateArray().Any(s => String(s, "side_data_type") == DolbyVisionSideData))
        {
            return "Dolby Vision";
        }

        return String(stream, "color_transfer") switch
        {
            "smpte2084" => "HDR10",
            "arib-std-b67" => "HLG",
            _ => null,
        };
    }

    // mkvmerge writes the stream bitrate as a BPS tag, sometimes with a language suffix (BPS-eng).
    private static long? MatroskaBitRate(JsonElement? tags)
    {
        if (tags is not { } t)
        {
            return null;
        }

        foreach (var tag in t.EnumerateObject())
        {
            if ((tag.Name.Equals("BPS", StringComparison.OrdinalIgnoreCase)
                    || tag.Name.StartsWith("BPS-", StringComparison.OrdinalIgnoreCase))
                && tag.Value.ValueKind == JsonValueKind.String
                && long.TryParse(tag.Value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var bps))
            {
                return bps;
            }
        }

        return null;
    }

    // Tag keys vary in case between containers (language vs LANGUAGE).
    private static string? Tag(JsonElement? tags, string name)
    {
        if (tags is not { } t)
        {
            return null;
        }

        foreach (var tag in t.EnumerateObject())
        {
            if (tag.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && tag.Value.ValueKind == JsonValueKind.String)
            {
                return string.IsNullOrWhiteSpace(tag.Value.GetString()) ? null : tag.Value.GetString();
            }
        }

        return null;
    }

    private static bool Flag(JsonElement? element, string name) =>
        element is { ValueKind: JsonValueKind.Object } e && Int(e, name) == 1;

    private static string? String(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int? Int(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var i)
            ? i
            : null;

    // ffprobe prints most numbers as strings ("1.023000", "8122000") and uses "N/A" when it doesn't know.
    private static long? Long(JsonElement element, string name) => element.TryGetProperty(name, out var value)
        ? value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt64(out var n) => n,
            JsonValueKind.String when long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) => n,
            _ => null,
        }
        : null;

    private static double? Double(JsonElement element, string name) => element.TryGetProperty(name, out var value)
        ? value.ValueKind switch
        {
            JsonValueKind.Number => value.GetDouble(),
            JsonValueKind.String when double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) => n,
            _ => null,
        }
        : null;
}
