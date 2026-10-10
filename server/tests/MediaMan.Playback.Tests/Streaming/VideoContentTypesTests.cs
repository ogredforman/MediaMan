using MediaMan.Playback.Streaming;

namespace MediaMan.Playback.Tests.Streaming;

public class VideoContentTypesTests
{
    [Theory]
    [InlineData("Alien.mkv", "video/x-matroska")]
    [InlineData("Alien.MP4", "video/mp4")]
    [InlineData("Alien.m2ts", "video/mp2t")]
    [InlineData("Alien.unknown", VideoContentTypes.Fallback)]
    [InlineData("Alien", VideoContentTypes.Fallback)]
    public void Types_follow_the_extension(string name, string expected)
    {
        VideoContentTypes.Of(Path.Combine("media", name)).Should().Be(expected);
    }
}
