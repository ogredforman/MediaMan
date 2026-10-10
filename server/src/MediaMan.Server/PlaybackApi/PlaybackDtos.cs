namespace MediaMan.Server.PlaybackApi;

/// <summary>What the client can play (contract <c>ClientCapabilities</c>); only <see cref="Player"/> matters for MPV.</summary>
public sealed record ClientCapabilities(
    string? Player,
    IReadOnlyList<string>? Containers = null,
    IReadOnlyList<string>? VideoCodecs = null,
    IReadOnlyList<string>? AudioCodecs = null,
    IReadOnlyList<string>? HdrFormats = null,
    long? MaxBitrate = null,
    IReadOnlyList<string>? SubtitleFormats = null);

public sealed record CreatePlaybackSessionRequest(Guid PlayableId, ClientCapabilities? Capabilities);

public sealed record PlaybackSessionDto(
    Guid PlayableId,
    Guid MediaFileId,
    string Method,
    string Url,
    string Title,
    double? ResumeSeconds,
    double? DurationSeconds,
    IReadOnlyList<SubtitleTrackDto> Subtitles);

public sealed record SubtitleTrackDto(string Url, string Format, string FileName, string? Language, bool Forced);
