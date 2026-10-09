namespace MediaMan.Server.LibraryApi;

/// <summary>
/// Rules for library root folders, shared by setup and the library API: a root is a readable folder given by its full
/// path, and roots never overlap, since a file belongs to one library. Only reads the folder (SPEC §5).
/// </summary>
public static class LibraryRoots
{
    /// <summary>Windows and macOS paths are case-insensitive by default; Linux paths aren't.</summary>
    public static StringComparison Comparison { get; } =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>Why the folder can't be a library root, or null with its normalized full path.</summary>
    public static string? PathProblem(string? path, out string rootPath)
    {
        rootPath = "";
        path = path?.Trim();
        if (string.IsNullOrEmpty(path))
        {
            return "Enter the folder that holds your media.";
        }

        if (!Path.IsPathFullyQualified(path))
        {
            return "Enter the full path to the folder, starting from the drive or root.";
        }

        rootPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (!Directory.Exists(rootPath))
        {
            return "This folder doesn't exist or can't be reached. Check the path, and that the drive is connected.";
        }

        try
        {
            using var entries = Directory.EnumerateFileSystemEntries(rootPath).GetEnumerator();
            entries.MoveNext();
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException)
        {
            return "MediaMan can't read this folder. Give the account the server runs as permission to read it, or pick another folder.";
        }

        return null;
    }

    /// <summary>Why the root can't hold the server's data directory, or null; the cache and database must stay out of External roots.</summary>
    public static string? DataDirectoryProblem(string rootPath, string dataDirectory) =>
        IsUnder(Path.GetFullPath(dataDirectory), rootPath)
            ? "This folder contains MediaMan's own data folder, which must stay outside your media. Pick a folder inside it instead."
            : null;

    /// <summary>True when the two folders are the same, or one is inside the other.</summary>
    public static bool Overlap(string a, string b) => IsUnder(a, b) || IsUnder(b, a);

    /// <summary>True when <paramref name="path"/> is <paramref name="root"/> or anything inside it. Both are full, normalized paths.</summary>
    public static bool IsUnder(string path, string root)
    {
        path = Path.TrimEndingDirectorySeparator(path);
        root = Path.TrimEndingDirectorySeparator(root);
        if (path.Equals(root, Comparison))
        {
            return true;
        }

        // A drive or filesystem root keeps its separator.
        var prefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, Comparison);
    }

    /// <summary>The folder's name; a drive or filesystem root has none, so it's named by its path.</summary>
    public static string NameOf(string rootPath) =>
        Path.GetFileName(rootPath) is { Length: > 0 } name ? name : rootPath;
}
