namespace MediaMan.Server.LibraryApi;

// Wire shapes for GET /home (contracts/openapi.yaml `Home` and friends).

public sealed record HomeDto(HomeHero? Hero, IReadOnlyList<HomeRow> Rows);

public sealed record HomeHero(
    TitleSummary Title,
    string? Overview,
    ArtworkRef? Backdrop,
    ArtworkRef? Logo,
    Guid? PlayableId,
    HomeEpisode? Episode,
    double? ResumeSeconds);

public sealed record HomeRow(string Kind, string? Genre, IReadOnlyList<HomeItem> Items)
{
    public const string ContinueWatching = "continueWatching";
    public const string NextUp = "nextUp";
    public const string RecentlyAdded = "recentlyAdded";
    public const string GenreKind = "genre";
}

public sealed record HomeItem(TitleSummary Title, Guid? PlayableId, HomeEpisode? Episode, HomeProgress? Progress);

public sealed record HomeEpisode(int SeasonNumber, int Number, string? Name, ArtworkRef? Still);

public sealed record HomeProgress(double PositionSeconds, double DurationSeconds);
