using MediaMan.Core.Domain;

namespace MediaMan.Playback.Versions;

/// <summary>A version's quality as a preference remembers it: resolution label (e.g. <c>1080p</c>) and HDR format (null for SDR).</summary>
public sealed record VersionQuality(string? Resolution, string? HdrFormat);

/// <summary>
/// Picks the version of a Playable to play (SPEC §7.4 step 2): the profile's preferred file if it's here, otherwise
/// the best version with the preferred resolution and HDR format (so one pick carries across a show's episodes),
/// otherwise the highest quality. Missing files never play. Pure; callers check the disk themselves.
/// </summary>
public static class VersionPicker
{
    public static MediaFile? Pick(IEnumerable<MediaFile> files, VersionPreference? preference)
    {
        var playable = files.Where(f => f.Status != MediaFileStatus.Missing).ToList();
        if (preference is not null)
        {
            if (preference.MediaFileId is { } id && playable.FirstOrDefault(f => f.Id == id) is { } exact)
            {
                return exact;
            }

            var wanted = new VersionQuality(preference.Resolution, preference.HdrFormat);
            if (Best(playable.Where(f => Same(QualityOf(f), wanted))) is { } matching)
            {
                return matching;
            }
        }

        return Best(playable);
    }

    /// <summary>
    /// The file's quality. The resolution follows the picture width like the web UI's label, so a 1920×800 scope
    /// film is 1080p; without a probe it falls back to what the file name says.
    /// </summary>
    public static VersionQuality QualityOf(MediaFile file)
    {
        var video = MainVideo(file);
        var resolution = video?.Width switch
        {
            >= 3200 => "2160p",
            >= 1800 => "1080p",
            >= 1200 => "720p",
            _ when video?.Height is { } height => $"{height}p",
            _ => file.Parsed?.Resolution switch
            {
                Resolution.P720 => "720p",
                Resolution.P1080 => "1080p",
                Resolution.P1440 => "1440p",
                Resolution.P2160 => "2160p",
                _ => null,
            },
        };
        return new VersionQuality(resolution, file.Probe?.HdrFormat ?? file.Parsed?.HdrFormat);
    }

    private static bool Same(VersionQuality a, VersionQuality b) =>
        string.Equals(a.Resolution, b.Resolution, StringComparison.OrdinalIgnoreCase)
        && string.Equals(a.HdrFormat, b.HdrFormat, StringComparison.OrdinalIgnoreCase);

    // Ranked by picture height, not the width-based label: of two "1080p" files, a full 1080-line one beats a scope encode.
    private static MediaFile? Best(IEnumerable<MediaFile> files) =>
        files
            .OrderByDescending(f => MainVideo(f)?.Height ?? 0)
            .ThenByDescending(f => f.SizeBytes)
            .ThenBy(f => f.Path, StringComparer.Ordinal)
            .FirstOrDefault();

    // Cover art shows up as a tiny video stream, so the main picture is the largest one.
    private static StreamInfo? MainVideo(MediaFile file) =>
        file.Probe?.Streams.Where(s => s.Type == MediaStreamType.Video).MaxBy(s => (long)(s.Width ?? 0) * (s.Height ?? 0));
}
