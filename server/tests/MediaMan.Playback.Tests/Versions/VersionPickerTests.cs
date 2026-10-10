using MediaMan.Core.Domain;
using MediaMan.Playback.Versions;

namespace MediaMan.Playback.Tests.Versions;

public class VersionPickerTests
{
    private static MediaFile File(string name, int width, int height, string? hdr = null, long size = 1_000,
        MediaFileStatus status = MediaFileStatus.Matched) =>
        new()
        {
            Id = Guid.NewGuid(),
            Path = Path.Combine("media", name),
            SizeBytes = size,
            Status = status,
            Probe = new ProbeResult
            {
                Container = "matroska,webm",
                HdrFormat = hdr,
                Streams =
                [
                    new StreamInfo { Index = 0, Type = MediaStreamType.Video, Codec = "hevc", Width = width, Height = height },
                    // Cover art: a tiny video stream that mustn't count as the picture.
                    new StreamInfo { Index = 1, Type = MediaStreamType.Video, Codec = "mjpeg", Width = 300, Height = 450 },
                ],
            },
        };

    private static VersionPreference Prefer(MediaFile file)
    {
        var quality = VersionPicker.QualityOf(file);
        return new VersionPreference { MediaFileId = file.Id, Resolution = quality.Resolution, HdrFormat = quality.HdrFormat };
    }

    [Fact]
    public void Without_a_preference_picks_the_highest_resolution_then_the_largest_file()
    {
        var hd = File("hd.mkv", 1920, 1080, size: 9_000);
        var uhdSmall = File("uhd-small.mkv", 3840, 2160, size: 4_000);
        var uhdLarge = File("uhd-large.mkv", 3840, 2160, size: 8_000);

        VersionPicker.Pick([hd, uhdSmall, uhdLarge], null).Should().BeSameAs(uhdLarge);
    }

    [Fact]
    public void Picks_the_preferred_file_when_it_is_here()
    {
        var hd = File("hd.mkv", 1920, 1080);
        var uhd = File("uhd.mkv", 3840, 2160, "HDR10");

        VersionPicker.Pick([hd, uhd], Prefer(hd)).Should().BeSameAs(hd);
    }

    [Fact]
    public void Picks_a_version_of_the_same_quality_for_another_episode()
    {
        var picked = File("s01e01.1080p.mkv", 1920, 1080);
        var otherHd = File("s01e02.1080p.mkv", 1920, 800);
        var otherUhd = File("s01e02.2160p.mkv", 3840, 2160, "HDR10");

        VersionPicker.Pick([otherUhd, otherHd], Prefer(picked)).Should().BeSameAs(otherHd);
    }

    [Fact]
    public void Matches_hdr_format_as_well_as_resolution()
    {
        var picked = File("e01.dv.mkv", 3840, 2160, "Dolby Vision");
        var hdr10 = File("e02.hdr10.mkv", 3840, 2160, "HDR10", size: 9_000);
        var dv = File("e02.dv.mkv", 3840, 2160, "dolby vision", size: 5_000);

        VersionPicker.Pick([hdr10, dv], Prefer(picked)).Should().BeSameAs(dv);
    }

    [Fact]
    public void Falls_back_to_the_highest_quality_when_nothing_matches()
    {
        var picked = File("e01.720p.mkv", 1280, 720);
        var hd = File("e02.1080p.mkv", 1920, 1080);
        var uhd = File("e02.2160p.mkv", 3840, 2160);

        VersionPicker.Pick([hd, uhd], Prefer(picked)).Should().BeSameAs(uhd);
    }

    [Fact]
    public void Never_picks_a_missing_file()
    {
        var uhd = File("uhd.mkv", 3840, 2160, status: MediaFileStatus.Missing);
        var hd = File("hd.mkv", 1920, 1080);

        VersionPicker.Pick([uhd, hd], Prefer(uhd)).Should().BeSameAs(hd);
        VersionPicker.Pick([uhd], null).Should().BeNull();
    }

    [Fact]
    public void A_preference_whose_file_is_gone_still_applies_by_quality()
    {
        var preference = new VersionPreference { MediaFileId = null, Resolution = "1080p" };
        var hd = File("hd.mkv", 1920, 1080);
        var uhd = File("uhd.mkv", 3840, 2160);

        VersionPicker.Pick([uhd, hd], preference).Should().BeSameAs(hd);
    }

    [Theory]
    [InlineData(3840, 1600, "2160p")]
    [InlineData(1920, 800, "1080p")]
    [InlineData(1280, 720, "720p")]
    [InlineData(720, 480, "480p")]
    public void Quality_labels_the_resolution_by_width(int width, int height, string expected) =>
        VersionPicker.QualityOf(File("x.mkv", width, height)).Resolution.Should().Be(expected);

    [Fact]
    public void Quality_falls_back_to_the_parsed_name_without_a_probe()
    {
        var file = new MediaFile
        {
            Path = "x.mkv",
            Parsed = new ParsedRelease { Title = "x", Resolution = Resolution.P2160, HdrFormat = "HDR10" },
        };

        VersionPicker.QualityOf(file).Should().Be(new VersionQuality("2160p", "HDR10"));
    }
}
