namespace MediaMan.Core.Domain;

public enum Resolution
{
    P720,
    P1080,
    P1440,
    P2160,
}

public enum DynamicRange
{
    Sdr,
    Hdr,
    DolbyVision,
}

public enum BitrateTier
{
    Low,
    Medium,
    High,
    Max,
}

public enum MediaStreamType
{
    Video,
    Audio,
    Subtitle,
    Attachment,
    Data,
}

/// <summary>Structured data extracted from a file or release name by <c>ReleaseParser</c>.</summary>
public sealed record ParsedRelease
{
    public string? Title { get; init; }
    public int? Year { get; init; }
    public int? Season { get; init; }
    public IReadOnlyList<int> Episodes { get; init; } = [];
    public int? AbsoluteEpisode { get; init; }
    public Resolution? Resolution { get; init; }
    public string? Source { get; init; }
    public string? Codec { get; init; }
    public string? HdrFormat { get; init; }
    public string? Audio { get; init; }
    public string? Group { get; init; }
}

/// <summary>What ffprobe reports about a file.</summary>
public sealed record ProbeResult
{
    public required string Container { get; init; }
    public TimeSpan? Duration { get; init; }
    public string? HdrFormat { get; init; }
    public IReadOnlyList<StreamInfo> Streams { get; init; } = [];
}

public sealed record StreamInfo
{
    public int Index { get; init; }
    public MediaStreamType Type { get; init; }
    public required string Codec { get; init; }
    public string? Language { get; init; }
    public string? Title { get; init; }
    public bool IsDefault { get; init; }
    public bool IsForced { get; init; }
    public int? Width { get; init; }
    public int? Height { get; init; }
    public int? Channels { get; init; }
    public long? BitRate { get; init; }
}

/// <summary>Quality derived from a ParsedRelease plus size and runtime (SPEC §8 tier table).</summary>
public sealed record ReleaseQuality
{
    public Resolution? Resolution { get; init; }
    public DynamicRange DynamicRange { get; init; }
    public BitrateTier? BitrateTier { get; init; }
    public string? Codec { get; init; }
    public string? Source { get; init; }
    public string? Audio { get; init; }
    public IReadOnlyList<string> Languages { get; init; } = [];
}

/// <summary>The quality chip selection on a "Get". An empty list means any value.</summary>
public sealed record QualityFilter
{
    public IReadOnlyList<Resolution> Resolutions { get; init; } = [];
    public IReadOnlyList<DynamicRange> DynamicRanges { get; init; } = [];
    public IReadOnlyList<BitrateTier> BitrateTiers { get; init; } = [];
    public IReadOnlyList<string> Codecs { get; init; } = [];
    public IReadOnlyList<string> Sources { get; init; } = [];
    public IReadOnlyList<string> Audio { get; init; } = [];
    public IReadOnlyList<string> Languages { get; init; } = [];
}
