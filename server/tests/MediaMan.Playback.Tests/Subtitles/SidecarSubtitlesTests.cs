using MediaMan.Playback.Subtitles;

namespace MediaMan.Playback.Tests.Subtitles;

public sealed class SidecarSubtitlesTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "mediaman-tests", Guid.NewGuid().ToString("N"));

    public SidecarSubtitlesTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void Finds_the_subtitles_named_after_the_video_with_their_tags()
    {
        var video = Touch("Alien.1979.1080p.mkv");
        Touch("Alien.1979.1080p.srt");
        Touch("Alien.1979.1080p.en.srt");
        Touch("Alien.1979.1080p.eng.forced.ass");
        Touch("Alien.1979.1080p.pt-BR.SDH.vtt");
        Touch("Alien.1979.1080p.fr.ssa");

        var found = SidecarSubtitles.Find(video);

        found.Select(s => (s.FileName, s.Format, s.Language, s.Forced)).Should().Equal(
            ("Alien.1979.1080p.en.srt", "srt", "en", false),
            ("Alien.1979.1080p.eng.forced.ass", "ass", "eng", true),
            ("Alien.1979.1080p.fr.ssa", "ssa", "fr", false),
            ("Alien.1979.1080p.pt-BR.SDH.vtt", "vtt", "pt-BR", false),
            ("Alien.1979.1080p.srt", "srt", null, false));
    }

    [Fact]
    public void Ignores_other_videos_subtitles_and_other_files()
    {
        var video = Touch("Show.S01E01.mkv");
        Touch("Show.S01E010.srt");
        Touch("Show.S01E01 extra.srt");
        Touch("Show.S01E02.en.srt");
        Touch("Show.S01E01.nfo");
        Touch("Show.S01E01.idx");

        SidecarSubtitles.Find(video).Should().BeEmpty();
    }

    [Fact]
    public void Matches_the_name_regardless_of_case()
    {
        var video = Touch("Movie.mkv");
        Touch("movie.EN.SRT");

        SidecarSubtitles.Find(video).Should().ContainSingle().Which.Should().Match<SidecarSubtitle>(s => s.Language == "EN" && s.Format == "srt");
    }

    [Fact]
    public void Ids_are_stable_hex_and_differ_per_file()
    {
        var video = Touch("Movie.mkv");
        Touch("Movie.en.srt");
        Touch("Movie.de.srt");

        var first = SidecarSubtitles.Find(video).Select(s => s.Id).ToList();
        var again = SidecarSubtitles.Find(video).Select(s => s.Id).ToList();

        first.Should().Equal(again).And.OnlyHaveUniqueItems().And.AllSatisfy(id => id.Should().MatchRegex("^[0-9a-f]{16}$"));
        first[0].Should().Be(SidecarSubtitles.IdOf("Movie.de.srt"));
    }

    [Fact]
    public void A_missing_folder_has_no_subtitles()
    {
        SidecarSubtitles.Find(Path.Combine(_folder, "gone", "Movie.mkv")).Should().BeEmpty();
    }

    [Fact]
    public void Content_types_follow_the_format()
    {
        var video = Path.Combine(_folder, "M.mkv");

        SidecarSubtitles.Parse(video, Path.Combine(_folder, "M.srt"))!.ContentType.Should().Be("application/x-subrip");
        SidecarSubtitles.Parse(video, Path.Combine(_folder, "M.vtt"))!.ContentType.Should().Be("text/vtt");
        SidecarSubtitles.Parse(video, Path.Combine(_folder, "M.ass"))!.ContentType.Should().Be("text/x-ssa");
    }

    private string Touch(string name)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllText(path, "1\n00:00:01,000 --> 00:00:02,000\nHi\n");
        return path;
    }
}
