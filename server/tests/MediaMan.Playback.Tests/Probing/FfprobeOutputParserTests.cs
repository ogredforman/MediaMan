using System.Globalization;
using MediaMan.Core.Domain;
using MediaMan.Playback.Probing;

namespace MediaMan.Playback.Tests.Probing;

public class FfprobeOutputParserTests
{
    private static ProbeResult ParseFixture(string name) =>
        FfprobeOutputParser.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "ffprobe", name)));

    [Fact]
    public void Parses_container_duration_and_streams()
    {
        var result = ParseFixture("hd1080-mkv.json");

        result.Container.Should().Be("matroska,webm");
        result.Duration.Should().Be(TimeSpan.FromSeconds(2699.987));
        result.HdrFormat.Should().BeNull();
        result.Streams.Should().BeEquivalentTo(
            [
                new StreamInfo
                {
                    Index = 0, Type = MediaStreamType.Video, Codec = "h264", Width = 1920, Height = 1080, IsDefault = true,
                    BitRate = 8_123_456,
                },
                new StreamInfo
                {
                    Index = 1, Type = MediaStreamType.Audio, Codec = "eac3", Language = "eng", Title = "Surround 5.1",
                    Channels = 6, IsDefault = true, BitRate = 640_000,
                },
                new StreamInfo
                {
                    Index = 2, Type = MediaStreamType.Subtitle, Codec = "subrip", Language = "fre", Title = "Forced", IsForced = true,
                },
                new StreamInfo { Index = 3, Type = MediaStreamType.Attachment, Codec = "unknown" },
            ],
            o => o.WithStrictOrdering());
    }

    [Theory]
    [InlineData("uhd-hdr10-mkv.json", "HDR10")]
    [InlineData("uhd-dolby-vision-mp4.json", "Dolby Vision")]
    [InlineData("hlg-ts.json", "HLG")]
    [InlineData("hd1080-mkv.json", null)]
    [InlineData("cover-art-mp4.json", null)]
    public void Detects_the_hdr_format(string fixture, string? expected)
    {
        ParseFixture(fixture).HdrFormat.Should().Be(expected);
    }

    [Fact]
    public void Reads_numeric_bitrates_and_data_streams()
    {
        var result = ParseFixture("hlg-ts.json");

        result.Container.Should().Be("mpegts");
        result.Streams.Select(s => s.Type).Should().Equal(MediaStreamType.Video, MediaStreamType.Audio, MediaStreamType.Data);
        ParseFixture("uhd-hdr10-mkv.json").Streams[1].BitRate.Should().Be(4_500_000);
    }

    [Fact]
    public void Treats_embedded_cover_art_as_an_attachment_and_unknown_duration_as_null()
    {
        var result = ParseFixture("cover-art-mp4.json");

        result.Duration.Should().BeNull();
        result.Streams.Where(s => s.Type == MediaStreamType.Video).Should().ContainSingle().Which.Codec.Should().Be("h264");
        result.Streams[2].Type.Should().Be(MediaStreamType.Attachment);
    }

    [Fact]
    public void Number_parsing_ignores_the_current_culture()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
        try
        {
            ParseFixture("hd1080-mkv.json").Duration.Should().Be(TimeSpan.FromSeconds(2699.987));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("{ \"streams\": [] }")]
    public void Malformed_output_is_invalid_data(string json)
    {
        var parse = () => FfprobeOutputParser.Parse(json);

        parse.Should().Throw<InvalidDataException>();
    }
}
