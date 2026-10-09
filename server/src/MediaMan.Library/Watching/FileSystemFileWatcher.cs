using MediaMan.Core.Modules;

namespace MediaMan.Library.Watching;

/// <summary>
/// The real <see cref="IFileWatcher"/> over <see cref="FileSystemWatcher"/>, recursive under each root. It only listens;
/// it never writes under a root (SPEC §5). A lost-events error (buffer overflow, a share dropping) is reported as a
/// <see cref="FileChangeKind.Changed"/> on the root itself, so the caller rescans the whole library.
/// </summary>
public sealed class FileSystemFileWatcher : IFileWatcher
{
    // Larger than the 8 KB default so a big copy into a library is less likely to overflow; the maximum is 64 KB.
    private const int BufferSize = 64 * 1024;

    /// <exception cref="DirectoryNotFoundException">The root doesn't exist.</exception>
    /// <exception cref="IOException">The OS refused the watch, e.g. the Linux inotify watch limit was reached.</exception>
    public IDisposable Watch(string root, Action<FileChange> onChange)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentNullException.ThrowIfNull(onChange);
        var full = Path.GetFullPath(root);
        if (!Directory.Exists(full))
        {
            throw new DirectoryNotFoundException($"The folder {full} doesn't exist, so it can't be watched.");
        }

        var watcher = new FileSystemWatcher(full)
        {
            IncludeSubdirectories = true,
            InternalBufferSize = BufferSize,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size | NotifyFilters.LastWrite,
        };
        watcher.Created += (_, e) => onChange(new FileChange(FileChangeKind.Created, e.FullPath));
        watcher.Changed += (_, e) => onChange(new FileChange(FileChangeKind.Changed, e.FullPath));
        watcher.Deleted += (_, e) => onChange(new FileChange(FileChangeKind.Deleted, e.FullPath));
        watcher.Renamed += (_, e) => onChange(new FileChange(FileChangeKind.Renamed, e.FullPath, e.OldFullPath));
        watcher.Error += (_, _) => onChange(new FileChange(FileChangeKind.Changed, full));
        try
        {
            watcher.EnableRaisingEvents = true;
        }
        catch (Exception e) when (e is ArgumentException or PlatformNotSupportedException)
        {
            watcher.Dispose();
            throw new IOException($"The folder {full} can't be watched: {e.Message}", e);
        }
        catch
        {
            watcher.Dispose();
            throw;
        }

        return watcher;
    }
}
