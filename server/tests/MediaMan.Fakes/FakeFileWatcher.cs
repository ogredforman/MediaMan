using MediaMan.Core.Modules;

namespace MediaMan.Fakes;

/// <summary>An <see cref="IFileWatcher"/> driven by <see cref="Raise"/> instead of the OS (SPEC §10.4).</summary>
public sealed class FakeFileWatcher : IFileWatcher
{
    private static readonly StringComparison _pathComparison =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private readonly Lock _gate = new();
    private readonly List<Registration> _registrations = [];

    /// <summary>Roots currently watched, in registration order.</summary>
    public IReadOnlyList<string> WatchedRoots
    {
        get
        {
            lock (_gate)
            {
                return [.. _registrations.Select(r => r.Root)];
            }
        }
    }

    public IDisposable Watch(string root, Action<FileChange> onChange)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentNullException.ThrowIfNull(onChange);
        var registration = new Registration(this, Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)), onChange);
        lock (_gate)
        {
            _registrations.Add(registration);
        }

        return registration;
    }

    /// <summary>Delivers <paramref name="change"/> to every watcher whose root contains its path (or old path, for renames).</summary>
    public void Raise(FileChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        Registration[] targets;
        lock (_gate)
        {
            targets = [.. _registrations.Where(r => r.Covers(change.Path) || (change.OldPath is { } old && r.Covers(old)))];
        }

        foreach (var target in targets)
        {
            target.OnChange(change);
        }
    }

    private void Remove(Registration registration)
    {
        lock (_gate)
        {
            _registrations.Remove(registration);
        }
    }

    private sealed class Registration(FakeFileWatcher owner, string root, Action<FileChange> onChange) : IDisposable
    {
        public string Root { get; } = root;

        public Action<FileChange> OnChange { get; } = onChange;

        public bool Covers(string path)
        {
            var full = Path.GetFullPath(path);
            return full.Equals(Root, _pathComparison)
                || (full.StartsWith(Root, _pathComparison) && full.Length > Root.Length
                    && (full[Root.Length] == Path.DirectorySeparatorChar || full[Root.Length] == Path.AltDirectorySeparatorChar
                        || Root.EndsWith(Path.DirectorySeparatorChar)));
        }

        public void Dispose() => owner.Remove(this);
    }
}
