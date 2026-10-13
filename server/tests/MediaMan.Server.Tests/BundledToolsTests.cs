using MediaMan.Server.Hosting;

namespace MediaMan.Server.Tests;

/// <summary>Which ffprobe the server runs (SPEC §9 bundled tools): the setting, then the bundled copy, then PATH.</summary>
public sealed class BundledToolsTests
{
    private static readonly string _baseDirectory = Path.Combine(Path.GetTempPath(), "mediaman-app");

    [Fact]
    public void A_configured_path_wins_over_the_bundled_copy()
    {
        var path = BundledTools.ResolveFfprobe("/opt/ffmpeg/ffprobe", _baseDirectory, _ => true, isWindows: true);

        path.Should().Be("/opt/ffmpeg/ffprobe");
    }

    [Fact]
    public void The_bundled_copy_beside_the_server_is_the_default()
    {
        var bundled = Path.Combine(_baseDirectory, "ffprobe.exe");

        var path = BundledTools.ResolveFfprobe(null, _baseDirectory, p => p == bundled, isWindows: true);

        path.Should().Be(bundled);
    }

    [Fact]
    public void Without_a_bundled_copy_ffprobe_is_looked_up_on_path()
    {
        var path = BundledTools.ResolveFfprobe(" ", _baseDirectory, _ => false, isWindows: true);

        path.Should().Be("ffprobe");
    }

    [Fact]
    public void The_bundled_copy_has_no_exe_suffix_outside_windows()
    {
        var checkedPaths = new List<string>();

        BundledTools.ResolveFfprobe(null, _baseDirectory, p => { checkedPaths.Add(p); return false; }, isWindows: false);

        checkedPaths.Should().Equal(Path.Combine(_baseDirectory, "ffprobe"));
    }
}
