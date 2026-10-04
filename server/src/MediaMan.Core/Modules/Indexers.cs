using MediaMan.Core.Domain;

namespace MediaMan.Core.Modules;

/// <summary>One configured torrent search source (Cardigann or Torznab).</summary>
public interface IIndexer
{
    /// <summary>The configured <see cref="Indexer.Id"/> in string form (<c>Guid.ToString("D")</c>); also used as <see cref="ReleaseResult.IndexerId"/>.</summary>
    string Id { get; }

    Task<IReadOnlyList<ReleaseResult>> SearchAsync(SearchQuery q, CancellationToken ct);

    Task<IndexerHealth> TestAsync(CancellationToken ct);
}

/// <summary>All indexer HTTP traffic goes through here; FlareSolverr sits behind it.</summary>
public interface IIndexerHttp
{
    Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, IndexerHttpOptions o, CancellationToken ct);
}

/// <summary>The community Cardigann definition repository.</summary>
public interface IDefinitionSource
{
    Task<IReadOnlyList<DefinitionFile>> FetchAsync(Uri source, CancellationToken ct);
}

/// <summary>Derives quality chips from a parsed release (pure; SPEC §8 bitrate tier table).</summary>
public interface IQualityClassifier
{
    ReleaseQuality Classify(ParsedRelease r, long sizeBytes, TimeSpan? runtime);
}

public sealed record SearchQuery
{
    public required string Term { get; init; }
    public TitleKind? Kind { get; init; }
    public int? Year { get; init; }
    public int? Season { get; init; }
    public int? Episode { get; init; }
    public IReadOnlyList<ExternalId> ExternalIds { get; init; } = [];
}

public sealed record IndexerHttpOptions(bool UseFlareSolverr, TimeSpan? Timeout = null);

public sealed record DefinitionFile(string Key, string? Version, string Yaml, Uri SourceUrl);
