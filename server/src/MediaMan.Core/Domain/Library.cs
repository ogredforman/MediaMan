namespace MediaMan.Core.Domain;

public enum LibraryKind
{
    External,
    Managed,
}

public enum ContentType
{
    Movies,
    Shows,
    Anime,
    Mixed,
}

public enum MediaFileStatus
{
    Matched,
    NeedsAttention,
    Ignored,
    Missing,
}

/// <summary>A set of root folders scanned as one library. External roots are never written to (SPEC §5).</summary>
public class Library
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public LibraryKind Kind { get; set; }
    public ContentType ContentType { get; set; }
    public List<string> RootPaths { get; set; } = [];
    public int ScanIntervalMinutes { get; set; }
}

/// <summary>A video file found on disk.</summary>
public class MediaFile
{
    public Guid Id { get; set; }
    public Guid LibraryId { get; set; }
    public required string Path { get; set; }
    public long SizeBytes { get; set; }
    public DateTimeOffset Mtime { get; set; }
    public ProbeResult? Probe { get; set; }
    public ParsedRelease? Parsed { get; set; }
    public MediaFileStatus Status { get; set; }
}

/// <summary>Links a MediaFile to a Playable it contains; <see cref="Part"/> orders multi-episode files.</summary>
public class FileLink
{
    public Guid MediaFileId { get; set; }
    public Guid PlayableId { get; set; }
    public int Part { get; set; }
}

/// <summary>A low-confidence or failed match awaiting a user.</summary>
public class MatchReview
{
    public Guid Id { get; set; }
    public Guid MediaFileId { get; set; }
    public List<MatchCandidate> Candidates { get; set; } = [];
    public required string Reason { get; set; }
}

public sealed record MatchCandidate(ExternalId TitleRef, double Score);
