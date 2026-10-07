using System.Text.Json.Serialization;

namespace MediaMan.Metadata.Tmdb;

// The parts of TMDB's v3 responses MediaMan reads, deserialized with snake_case names.

internal sealed record TmdbSearchPage(IReadOnlyList<TmdbSearchResult> Results);

internal sealed record TmdbSearchResult(
    int Id,
    string? Title,
    string? Name,
    string? ReleaseDate,
    string? FirstAirDate,
    string? Overview,
    string? PosterPath);

internal sealed record TmdbGenre(string Name);

internal sealed record TmdbExternalIds(string? ImdbId, int? TvdbId);

internal sealed record TmdbImage(string FilePath, [property: JsonPropertyName("iso_639_1")] string? Iso6391);

internal sealed record TmdbImages(
    IReadOnlyList<TmdbImage>? Posters,
    IReadOnlyList<TmdbImage>? Backdrops,
    IReadOnlyList<TmdbImage>? Logos);

internal sealed record TmdbSeasonSummary(int SeasonNumber, string? Name, int EpisodeCount);

/// <summary>A movie or a TV show from <c>/movie/{id}</c> or <c>/tv/{id}</c> with external ids and images appended.</summary>
internal sealed record TmdbTitle(
    int Id,
    string? Title,
    string? Name,
    string? ReleaseDate,
    string? FirstAirDate,
    string? Overview,
    IReadOnlyList<TmdbGenre>? Genres,
    double VoteAverage,
    int VoteCount,
    int? Runtime,
    IReadOnlyList<int>? EpisodeRunTime,
    string? PosterPath,
    string? BackdropPath,
    IReadOnlyList<TmdbSeasonSummary>? Seasons,
    TmdbExternalIds? ExternalIds,
    TmdbImages? Images);

internal sealed record TmdbEpisode(int EpisodeNumber, string? Name, string? Overview, string? AirDate, string? StillPath);

internal sealed record TmdbSeason(
    int SeasonNumber,
    string? Name,
    string? Overview,
    string? PosterPath,
    IReadOnlyList<TmdbEpisode>? Episodes);
