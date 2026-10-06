using System.ComponentModel;
using System.Diagnostics;
using MediaMan.Core.Domain;
using MediaMan.Core.Modules;

namespace MediaMan.Playback.Probing;

/// <summary>
/// The real <see cref="IMediaProbe"/>: runs ffprobe as a separate process (SPEC §10.1) and parses its JSON.
/// Errors match <c>FakeMediaProbe</c>: a missing file throws <see cref="FileNotFoundException"/> and an unreadable
/// one <see cref="InvalidDataException"/>. A missing ffprobe throws <see cref="InvalidOperationException"/>,
/// which is a setup problem, not a property of the file.
/// </summary>
public sealed class FfprobeMediaProbe(FfprobeOptions options) : IMediaProbe
{
    public async Task<ProbeResult> ProbeAsync(string path, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"'{path}' does not exist.", path);
        }

        var startInfo = new ProcessStartInfo(options.ExecutablePath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in (string[])["-v", "error", "-print_format", "json", "-show_format", "-show_streams", "-i", Path.GetFullPath(path)])
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (Win32Exception e)
        {
            throw new InvalidOperationException(
                $"ffprobe wasn't found at '{options.ExecutablePath}'. Install ffmpeg, or set MediaMan:Tools:FfprobePath to the ffprobe binary.",
                e);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(options.Timeout);
        string stdout;
        string stderr;
        try
        {
            var stdoutTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            stdout = await stdoutTask.ConfigureAwait(false);
            stderr = await stderrTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Kill(process);
            ct.ThrowIfCancellationRequested();
            throw new InvalidDataException(
                $"ffprobe took longer than {options.Timeout.TotalSeconds:0} seconds on '{Path.GetFileName(path)}'.");
        }

        if (process.ExitCode != 0)
        {
            var reason = stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .LastOrDefault() ?? $"exit code {process.ExitCode}";
            throw new InvalidDataException($"ffprobe couldn't read '{Path.GetFileName(path)}': {reason}");
        }

        return FfprobeOutputParser.Parse(stdout);
    }

    private static void Kill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Already exited.
        }
    }
}
