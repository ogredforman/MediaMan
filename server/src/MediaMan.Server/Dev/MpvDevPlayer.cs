using System.ComponentModel;
using System.Diagnostics;

namespace MediaMan.Server.Dev;

/// <summary>
/// Launches MPV as a separate process on the server's host (SPEC §10.1) and leaves it running; the user closes it.
/// Arguments go through <see cref="ProcessStartInfo.ArgumentList"/>, so no shell parses the path.
/// </summary>
public sealed partial class MpvDevPlayer(string executablePath, ILogger<MpvDevPlayer> logger) : IDevPlayer
{
    public Task PlayAsync(string path, string title, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var start = new ProcessStartInfo(executablePath) { UseShellExecute = false };
        start.ArgumentList.Add("--force-window=yes");
        start.ArgumentList.Add($"--title={title}");
        start.ArgumentList.Add("--"); // a file name starting with '-' is never read as an option
        start.ArgumentList.Add(path);

        try
        {
            using var process = Process.Start(start)
                ?? throw new DevPlayerException($"MPV ('{executablePath}') didn't start.");
            LogLaunched(logger, process.Id, path);
        }
        catch (Win32Exception e)
        {
            throw new DevPlayerException($"MPV ('{executablePath}') couldn't be started: {e.Message}", e);
        }

        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Launched MPV (pid {ProcessId}) for {Path}")]
    private static partial void LogLaunched(ILogger logger, int processId, string path);
}
