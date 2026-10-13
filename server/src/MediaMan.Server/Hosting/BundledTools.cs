namespace MediaMan.Server.Hosting;

/// <summary>
/// Finds the media tools a distribution ships next to the server (SPEC §9 bundled tools, §10.1). A configured path
/// always wins; otherwise the bundled copy beside the server executable; otherwise the bare name, looked up on PATH.
/// </summary>
public static class BundledTools
{
    public static string ResolveFfprobe(string? configured, string baseDirectory, Func<string, bool> fileExists, bool isWindows)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        var name = isWindows ? "ffprobe.exe" : "ffprobe";
        var bundled = Path.Combine(baseDirectory, name);
        return fileExists(bundled) ? bundled : "ffprobe";
    }
}
