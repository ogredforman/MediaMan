namespace MediaMan.Core.Domain;

public enum IndexerType
{
    Cardigann,
    Torznab,
}

public enum IndexerHealthStatus
{
    Unknown,
    Healthy,
    Degraded,
    Failing,
}

public sealed record IndexerHealth(IndexerHealthStatus Status, string? Message, DateTimeOffset CheckedAt);

/// <summary>A configured torrent search source.</summary>
public class Indexer
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public IndexerType Type { get; set; }
    public Guid? DefinitionId { get; set; }
    public Dictionary<string, string> Settings { get; set; } = [];
    public bool Enabled { get; set; }
    public bool UseFlareSolverr { get; set; }
    public IndexerHealth? Health { get; set; }
}

/// <summary>An imported Cardigann YAML definition.</summary>
public class IndexerDefinition
{
    public Guid Id { get; set; }
    public required string Key { get; set; }
    public required string Version { get; set; }
    public required string Yaml { get; set; }
    public required string SourceUrl { get; set; }
}

/// <summary>A search result (transient, cached briefly). One of <see cref="MagnetUri"/> and <see cref="TorrentUrl"/> is set.</summary>
public sealed record ReleaseResult
{
    /// <summary>The source <see cref="Indexer.Id"/> as <c>Guid.ToString("D")</c> (matches <c>IIndexer.Id</c>).</summary>
    public required string IndexerId { get; init; }
    public required string Title { get; init; }
    public string? MagnetUri { get; init; }
    public string? TorrentUrl { get; init; }
    public long SizeBytes { get; init; }
    public int Seeders { get; init; }
    public int Leechers { get; init; }
    public DateTimeOffset? PublishDate { get; init; }
    public required ParsedRelease Parsed { get; init; }
    public IReadOnlyList<string>? DangerousFiles { get; init; }
    public ReleaseQuality? Quality { get; init; }
}
