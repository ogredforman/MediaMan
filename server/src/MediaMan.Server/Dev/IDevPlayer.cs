namespace MediaMan.Server.Dev;

/// <summary>
/// Plays a file on the server's own host for the dev-only play endpoint (SPEC §8 playback [P], §13). A prototype seam
/// for the MPV process, not a SPEC §10.3 interface: it never ships, and the desktop app's <c>IPlayer</c> replaces it.
/// </summary>
public interface IDevPlayer
{
    /// <summary>Starts playback and returns once the player is launched; throws <see cref="DevPlayerException"/> if it can't start.</summary>
    Task PlayAsync(string path, string title, CancellationToken ct);
}

/// <summary>The player couldn't be started; the message says why.</summary>
public sealed class DevPlayerException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Records plays instead of launching anything, for tests and for running on the fakes.</summary>
public sealed class FakeDevPlayer : IDevPlayer
{
    private readonly Lock _gate = new();
    private readonly List<(string Path, string Title)> _plays = [];
    private string? _failWith;

    public IReadOnlyList<(string Path, string Title)> Plays
    {
        get
        {
            lock (_gate)
            {
                return [.. _plays];
            }
        }
    }

    /// <summary>Makes the next play fail as if MPV couldn't start.</summary>
    public void FailNext(string message)
    {
        lock (_gate)
        {
            _failWith = message;
        }
    }

    public Task PlayAsync(string path, string title, CancellationToken ct)
    {
        lock (_gate)
        {
            if (_failWith is { } message)
            {
                _failWith = null;
                throw new DevPlayerException(message);
            }

            _plays.Add((path, title));
        }

        return Task.CompletedTask;
    }
}
