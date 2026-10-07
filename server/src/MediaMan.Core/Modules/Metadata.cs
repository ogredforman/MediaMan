using MediaMan.Core.Domain;

namespace MediaMan.Core.Modules;

/// <summary>A metadata source such as TMDB or AniList (SPEC §10.3).</summary>
public interface IMetadataProvider
{
    /// <summary>Provider key, e.g. <c>tmdb</c> or <c>anilist</c>; matches <see cref="ExternalId.Provider"/>.</summary>
    string Key { get; }

    Task<IReadOnlyList<TitleCandidate>> SearchAsync(TitleQuery q, CancellationToken ct);

    Task<TitleDetails> GetTitleAsync(ExternalId id, CancellationToken ct);

    Task<SeasonDetails> GetSeasonAsync(ExternalId showId, int season, CancellationToken ct);
}

/// <summary>Extra artwork, e.g. fanart.tv.</summary>
public interface IArtworkProvider
{
    Task<IReadOnlyList<ArtworkRef>> GetArtworkAsync(ExternalId id, CancellationToken ct);
}

/// <summary>
/// Fetches artwork images by the URL a metadata provider gave (SPEC §10.3). Throws <see cref="HttpRequestException"/>
/// when the image can't be fetched (including timeouts) and <see cref="InvalidDataException"/> when it's too large.
/// </summary>
public interface IImageDownloader
{
    Task<byte[]> DownloadAsync(Uri url, CancellationToken ct);
}

/// <summary>Subtitle search and download, e.g. OpenSubtitles.</summary>
public interface ISubtitleProvider
{
    Task<IReadOnlyList<SubtitleCandidate>> SearchAsync(SubtitleQuery q, CancellationToken ct);

    Task<Stream> DownloadAsync(string candidateId, CancellationToken ct);
}

public sealed record TitleQuery(string Name, int? Year, TitleKind? Kind);

public sealed record TitleCandidate(
    ExternalId Id,
    TitleKind Kind,
    string Name,
    int? Year,
    string? Overview,
    string? PosterUrl);

public sealed record TitleDetails
{
    public required ExternalId Id { get; init; }
    public TitleKind Kind { get; init; }
    public required string Name { get; init; }
    public int? Year { get; init; }
    public string? Overview { get; init; }
    public IReadOnlyList<string> Genres { get; init; } = [];
    public double? Rating { get; init; }
    public int? RuntimeMinutes { get; init; }

    /// <summary>Cross-references at other providers (imdb, tvdb, ...).</summary>
    public IReadOnlyList<ExternalId> ExternalIds { get; init; } = [];

    public IReadOnlyList<SeasonSummary> Seasons { get; init; } = [];
    public IReadOnlyList<ArtworkRef> Artwork { get; init; } = [];
}

public sealed record SeasonSummary(int Number, string? Name, int EpisodeCount);

public sealed record SeasonDetails
{
    public required ExternalId ShowId { get; init; }
    public int Number { get; init; }
    public string? Name { get; init; }
    public string? Overview { get; init; }
    public string? PosterUrl { get; init; }
    public IReadOnlyList<EpisodeDetails> Episodes { get; init; } = [];
}

public sealed record EpisodeDetails(
    int Number,
    int? AbsoluteNumber,
    string? Name,
    string? Overview,
    DateOnly? AirDate,
    string? StillUrl);

public sealed record ArtworkRef(ArtworkType Type, string Url, string? Language);

public sealed record SubtitleQuery
{
    public ExternalId? TitleId { get; init; }
    public int? Season { get; init; }
    public int? Episode { get; init; }
    public string? FileHash { get; init; }
    public IReadOnlyList<string> Languages { get; init; } = [];
}

public sealed record SubtitleCandidate(
    string Id,
    string Language,
    string Format,
    string? ReleaseName,
    bool HearingImpaired);
