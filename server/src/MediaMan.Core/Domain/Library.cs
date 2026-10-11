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
    Pending,
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

    /// <summary>When the scan in progress started; still set at startup means the last scan was interrupted, so it resumes.</summary>
    public DateTimeOffset? ScanStartedAt { get; set; }

    /// <summary>When the last scan finished, succeeded or failed; the periodic full scan is due <see cref="ScanIntervalMinutes"/> after it.</summary>
    public DateTimeOffset? LastScanFinishedAt { get; set; }
}

/// <summary>A video file found on disk.</summary>
public class MediaFile
{
    public Guid Id { get; set; }
    public Guid LibraryId { get; set; }
    public required string Path { get; set; }
    public long SizeBytes { get; set; }
    public DateTimeOffset Mtime { get; set; }

    /// <summary>When a scan first found the file; Recently Added sorts by it (SPEC §8 browsing).</summary>
    public DateTimeOffset AddedAt { get; set; }

    public ProbeResult? Probe { get; set; }
    public ParsedRelease? Parsed { get; set; }
    public MediaFileStatus Status { get; set; }

    /// <summary>Why the file is <see cref="MediaFileStatus.NeedsAttention"/>, e.g. <c>unreadable</c>; null otherwise.</summary>
    public string? AttentionReason { get; set; }

    /// <summary>When the file was first found missing; FileLinks are kept for 30 days from then (SPEC §7.2 step 6).</summary>
    public DateTimeOffset? MissingSince { get; set; }
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

/// <summary>A scored candidate. Kind, name, year and poster are the provider's, kept so a review can show them without a lookup.</summary>
public sealed record MatchCandidate(
    ExternalId TitleRef,
    double Score,
    TitleKind? Kind = null,
    string? Name = null,
    int? Year = null,
    string? PosterUrl = null);

/// <summary>
/// A user's "Fix match" choice for a path (SPEC §7.3). When the file at <see cref="Path"/> is matched again (changed,
/// restored or found anew) the matcher links it as recorded here instead of searching, so rescans keep the choice.
/// </summary>
public class MatchOverride
{
    public Guid Id { get; set; }
    public Guid LibraryId { get; set; }
    public required string Path { get; set; }
    public ExternalId TitleRef { get; set; }
    public TitleKind Kind { get; set; }

    /// <summary>The show's season for <see cref="Episodes"/>; null for movies and absolute-numbered episodes.</summary>
    public int? Season { get; set; }

    /// <summary>The episodes the file holds, in order; empty for movies and absolute-numbered episodes.</summary>
    public List<int> Episodes { get; set; } = [];

    /// <summary>The absolute episode number when the file is numbered that way (anime) and no season mapping was given.</summary>
    public int? AbsoluteEpisode { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
