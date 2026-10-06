namespace MediaMan.Library.Scanning;

/// <summary>Which files and folders a scan considers (SPEC §7.2 step 1). Pure: decisions use only names and sizes.</summary>
public static class ScanRules
{
    /// <summary>A file with <c>sample</c> in its name is a sample only below this size, so a feature titled "Sample" still counts.</summary>
    public const long SampleMaxBytes = 300L * 1024 * 1024;

    private static readonly HashSet<string> _videoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mkv", ".mp4", ".m4v", ".avi", ".mov", ".wmv", ".ts", ".m2ts", ".webm", ".mpg", ".mpeg", ".vob", ".ogv", ".flv",
    };

    // "Specials" and "Season 00" are real season folders, not extras.
    private static readonly HashSet<string> _extrasFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Extras", "Featurettes", "Behind The Scenes", "Deleted Scenes", "Interviews", "Trailers", "Shorts", "Scenes",
        "Sample", "Samples",
    };

    public static bool IsVideo(string path) => _videoExtensions.Contains(Path.GetExtension(path));

    public static bool IsSample(string fileName, long sizeBytes) =>
        sizeBytes < SampleMaxBytes
        && Path.GetFileNameWithoutExtension(fileName).Contains("sample", StringComparison.OrdinalIgnoreCase);

    public static bool IsExtrasFolder(string folderName) => _extrasFolders.Contains(folderName.Trim());

    /// <summary>True when the scan should store and probe this file.</summary>
    public static bool ShouldScan(string fileName, long sizeBytes) => IsVideo(fileName) && !IsSample(fileName, sizeBytes);
}
