namespace MediaMan.Core.Domain;

public enum TitleKind
{
    Movie,
    Show,
}

public enum PlayableKind
{
    Movie,
    Episode,
}

public enum ArtworkType
{
    Poster,
    Backdrop,
    Logo,
    Still,
}

/// <summary>Provider ids for a Title. Any may be missing.</summary>
public class TitleExternalIds
{
    public string? Tmdb { get; set; }
    public string? Imdb { get; set; }
    public string? Tvdb { get; set; }
    public string? AniList { get; set; }
}

/// <summary>A movie or show.</summary>
public class Title
{
    public Guid Id { get; set; }
    public TitleKind Kind { get; set; }
    public required string Name { get; set; }
    public int? Year { get; set; }
    public string? Overview { get; set; }
    public List<string> Genres { get; set; } = [];
    public double? Rating { get; set; }
    public int? RuntimeMinutes { get; set; }
    public TitleExternalIds ExternalIds { get; set; } = new();
}

public class Season
{
    public Guid Id { get; set; }
    public Guid TitleId { get; set; }
    public int Number { get; set; }
    public string? Name { get; set; }
    public string? Overview { get; set; }
}

public class Episode
{
    public Guid Id { get; set; }
    public Guid SeasonId { get; set; }
    public int Number { get; set; }
    public int? AbsoluteNumber { get; set; }
    public string? Name { get; set; }
    public string? Overview { get; set; }
    public DateOnly? AirDate { get; set; }
}

/// <summary>
/// The thing a user watches: a movie Title or an Episode. Exactly one of <see cref="TitleId"/> and
/// <see cref="EpisodeId"/> is set, matching <see cref="Kind"/> (ADR 0002).
/// </summary>
public class Playable
{
    public Guid Id { get; set; }
    public PlayableKind Kind { get; set; }
    public Guid? TitleId { get; set; }
    public Guid? EpisodeId { get; set; }

    public static Playable ForMovie(Guid id, Guid titleId) =>
        new() { Id = id, Kind = PlayableKind.Movie, TitleId = titleId };

    public static Playable ForEpisode(Guid id, Guid episodeId) =>
        new() { Id = id, Kind = PlayableKind.Episode, EpisodeId = episodeId };
}

/// <summary>
/// A cached image. Exactly one owner FK is set (ADR 0002); a season poster or an episode still is an
/// Artwork row of that type.
/// </summary>
public class Artwork
{
    public Guid Id { get; set; }
    public Guid? TitleId { get; set; }
    public Guid? SeasonId { get; set; }
    public Guid? EpisodeId { get; set; }
    public ArtworkType Type { get; set; }
    public required string Url { get; set; }
    public string? LocalPath { get; set; }
    public string? DominantColor { get; set; }
}
