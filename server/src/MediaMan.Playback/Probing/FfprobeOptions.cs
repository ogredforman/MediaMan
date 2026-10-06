namespace MediaMan.Playback.Probing;

public sealed class FfprobeOptions
{
    /// <summary>The ffprobe binary: a full path, or a bare name looked up on PATH.</summary>
    public string ExecutablePath { get; init; } = "ffprobe";

    /// <summary>How long one probe may run before the process is killed and the file treated as unreadable.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(60);
}
