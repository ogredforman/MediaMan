using MediaMan.Library.Scanning;

namespace MediaMan.Library.Tests;

public class ScanRulesTests
{
    private const long Mb = 1024 * 1024;

    [Theory]
    [InlineData("Movie.2020.1080p.mkv", true)]
    [InlineData("Movie.2020.MP4", true)]
    [InlineData("show.s01e01.m2ts", true)]
    [InlineData("show.s01e01.avi", true)]
    [InlineData("Movie.2020.nfo", false)]
    [InlineData("Movie.2020.en.srt", false)]
    [InlineData("poster.jpg", false)]
    [InlineData("Movie.2020.mkv.part", false)]
    [InlineData("README", false)]
    public void Recognises_video_files_by_extension(string fileName, bool expected) =>
        ScanRules.IsVideo(fileName).Should().Be(expected);

    [Theory]
    [InlineData("movie-sample.mkv", 50 * Mb, true)]
    [InlineData("Sample.mkv", 299 * Mb, true)]
    [InlineData("movie.SAMPLE.mkv", 1, true)]
    [InlineData("movie-sample.mkv", 300 * Mb, false)]
    [InlineData("The.Sample.2023.1080p.mkv", 4000 * Mb, false)]
    [InlineData("movie.mkv", 10 * Mb, false)]
    public void Samples_need_the_word_and_a_small_size(string fileName, long size, bool expected) =>
        ScanRules.IsSample(fileName, size).Should().Be(expected);

    [Theory]
    [InlineData("Extras", true)]
    [InlineData("featurettes", true)]
    [InlineData("Behind The Scenes", true)]
    [InlineData("Deleted Scenes", true)]
    [InlineData("Trailers", true)]
    [InlineData("Sample", true)]
    [InlineData("Specials", false)]
    [InlineData("Season 00", false)]
    [InlineData("Season 1", false)]
    [InlineData("The Extras Movie (2020)", false)]
    public void Recognises_extras_folders(string folderName, bool expected) =>
        ScanRules.IsExtrasFolder(folderName).Should().Be(expected);
}
