namespace MediaMan.Core.Domain;

public enum DownloadState
{
    Queued,
    FetchingMetadata,
    Downloading,
    Paused,
    Seeding,
    Completed,
    Error,
}

public enum FilePriority
{
    Skip,
    Low,
    Normal,
    High,
}

public enum DownloadRequestStatus
{
    Pending,
    Approved,
    Denied,
}

public enum VpnMode
{
    Interface,
    Socks5,
    NoneAccepted,
}

/// <summary>A torrent being downloaded or seeded.</summary>
public class Download
{
    public Guid Id { get; set; }
    public InfoHash InfoHash { get; set; }
    public required string Name { get; set; }
    public DownloadState State { get; set; }
    public double Progress { get; set; }
    public required string SavePath { get; set; }
    public Guid? TargetTitleId { get; set; }
    public Guid RequestedBy { get; set; }
    public double? SeedRatioLimit { get; set; }
    public TimeSpan? SeedTimeLimit { get; set; }
}

/// <summary>A file inside a Download. <see cref="Index"/> is the file position within the torrent.</summary>
public class DownloadFile
{
    public Guid DownloadId { get; set; }
    public int Index { get; set; }
    public required string Path { get; set; }
    public long SizeBytes { get; set; }
    public FilePriority Priority { get; set; }
    public bool Skipped { get; set; }
    public bool Dangerous { get; set; }
}

/// <summary>A user asking for a download. The title may not be in the library yet, so it is an external ref.</summary>
public class DownloadRequest
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public ExternalId TitleRef { get; set; }
    public required ReleaseResult Release { get; set; }
    public QualityFilter Filter { get; set; } = new();
    public DownloadRequestStatus Status { get; set; }
    public Guid? DecidedBy { get; set; }
}

public class SavedQualityProfile
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public required string Name { get; set; }
    public QualityFilter Filter { get; set; } = new();
}

/// <summary>The torrent network path (SPEC §7.8). <see cref="SocksPassword"/> is a secret and must never be logged.</summary>
public class VpnConfig
{
    public VpnMode Mode { get; set; }
    public string? InterfaceId { get; set; }
    public string? SocksHost { get; set; }
    public int? SocksPort { get; set; }
    public string? SocksUsername { get; set; }
    public string? SocksPassword { get; set; }
    public int LeakCheckIntervalSeconds { get; set; }
}

/// <summary>Server-wide key/value.</summary>
public class Setting
{
    public required string Key { get; set; }
    public required string Value { get; set; }
}
