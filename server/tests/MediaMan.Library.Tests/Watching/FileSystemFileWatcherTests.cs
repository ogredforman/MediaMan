using System.Collections.Concurrent;
using MediaMan.Core.Modules;
using MediaMan.Library.Watching;

namespace MediaMan.Library.Tests.Watching;

public sealed class FileSystemFileWatcherTests : IDisposable
{
    private static readonly TimeSpan _wait = TimeSpan.FromSeconds(10);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "mediaman-tests", "watch-" + Guid.NewGuid().ToString("N"));
    private readonly ConcurrentQueue<FileChange> _changes = new();
    private readonly FileSystemFileWatcher _watcher = new();

    public FileSystemFileWatcherTests() => Directory.CreateDirectory(Path.Combine(_root, "Movies"));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task Reports_files_created_renamed_and_deleted_in_subfolders()
    {
        using var watch = _watcher.Watch(_root, _changes.Enqueue);
        var created = Path.Combine(_root, "Movies", "Dune.mkv");
        var renamed = Path.Combine(_root, "Movies", "Dune (2021).mkv");

        await File.WriteAllBytesAsync(created, [1, 2, 3], Ct);
        await WaitForAsync(c => c.Kind == FileChangeKind.Created && c.Path == created);
        File.Move(created, renamed);
        await WaitForAsync(c => c.Kind == FileChangeKind.Renamed && c.Path == renamed && c.OldPath == created);
        File.Delete(renamed);
        await WaitForAsync(c => c.Kind == FileChangeKind.Deleted && c.Path == renamed);
    }

    [Fact]
    public void A_missing_root_cant_be_watched()
    {
        var act = () => _watcher.Watch(Path.Combine(_root, "Gone"), _changes.Enqueue);

        act.Should().Throw<DirectoryNotFoundException>();
    }

    private async Task WaitForAsync(Func<FileChange, bool> match)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(_wait);
        while (!_changes.Any(match))
        {
            await Task.Delay(TimeSpan.FromMilliseconds(20), timeout.Token);
        }
    }
}
