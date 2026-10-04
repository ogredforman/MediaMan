namespace MediaMan.Core.Modules;

/// <summary>ffprobe, run as a separate process (SPEC §10.1).</summary>
public interface IMediaProbe
{
    Task<Domain.ProbeResult> ProbeAsync(string path, CancellationToken ct);
}

/// <summary>ffmpeg, run as a separate process: remux or transcode to HLS (SPEC §7.5).</summary>
public interface ITranscoder
{
    Task<StreamSession> StartAsync(StreamPlan plan, CancellationToken ct);

    Task StopAsync(string sessionId, CancellationToken ct);

    IReadOnlyList<HwEncoder> DetectEncoders();
}

/// <summary>Filesystem change notifications. The filesystem itself is not faked (SPEC §10.4).</summary>
public interface IFileWatcher
{
    IDisposable Watch(string root, Action<FileChange> onChange);
}

public enum PlaybackMode
{
    Direct,
    Remux,
    Transcode,
}

/// <summary>What to do with a file for one client; chosen by a pure function from the probe and client capabilities.</summary>
public sealed record StreamPlan
{
    public required string InputPath { get; init; }
    public PlaybackMode Mode { get; init; }
    public int? VideoStreamIndex { get; init; }
    public int? AudioStreamIndex { get; init; }
    public bool TranscodeVideo { get; init; }
    public bool TranscodeAudio { get; init; }
    public bool ToneMap { get; init; }
    public long? MaxBitrate { get; init; }
    public TimeSpan StartAt { get; init; }
    public HwEncoder? Encoder { get; init; }
}

public sealed record StreamSession(string Id, string PlaylistPath);

/// <summary>A hardware encoder ffmpeg can use, e.g. <c>h264_nvenc</c>.</summary>
public sealed record HwEncoder(string Name, string Codec);

public enum FileChangeKind
{
    Created,
    Changed,
    Deleted,
    Renamed,
}

/// <summary>A filesystem change. <see cref="OldPath"/> is set only for renames.</summary>
public sealed record FileChange(FileChangeKind Kind, string Path, string? OldPath = null);
