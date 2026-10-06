using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using MediaMan.Core;
using MediaMan.Core.Domain;
using MediaMan.Core.Modules;

namespace MediaMan.Fakes;

/// <summary>One title in <c>server/tests/fixtures/metadata/*.json</c>, shaped like a provider response.</summary>
public sealed record MetadataFixture
{
    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public required string Id { get; init; }
    public TitleKind Kind { get; init; }
    public required string Name { get; init; }
    public int? Year { get; init; }
    public string? Overview { get; init; }
    public IReadOnlyList<string> Genres { get; init; } = [];
    public double? Rating { get; init; }
    public int? RuntimeMinutes { get; init; }
    public IReadOnlyList<string> ExternalIds { get; init; } = [];
    public IReadOnlyList<FixtureArtwork> Artwork { get; init; } = [];
    public IReadOnlyList<FixtureSeason> Seasons { get; init; } = [];

    public static MetadataFixture Parse(string json) =>
        JsonSerializer.Deserialize<MetadataFixture>(json, _jsonOptions)
            ?? throw new InvalidDataException("Metadata fixture is empty.");

    public ExternalId ExternalId => ExternalId.Parse(Id);

    public TitleCandidate ToCandidate() =>
        new(ExternalId, Kind, Name, Year, Overview, Artwork.FirstOrDefault(a => a.Type == ArtworkType.Poster)?.Url);

    public TitleDetails ToDetails() => new()
    {
        Id = ExternalId,
        Kind = Kind,
        Name = Name,
        Year = Year,
        Overview = Overview,
        Genres = Genres,
        Rating = Rating,
        RuntimeMinutes = RuntimeMinutes,
        ExternalIds = [.. ExternalIds.Select(ExternalId.Parse)],
        Seasons = [.. Seasons.Select(s => new SeasonSummary(s.Number, s.Name, s.Episodes.Count))],
        Artwork = [.. Artwork.Select(a => new ArtworkRef(a.Type, a.Url, a.Language))],
    };
}

public sealed record FixtureArtwork(ArtworkType Type, string Url, string? Language);

public sealed record FixtureSeason
{
    public int Number { get; init; }
    public string? Name { get; init; }
    public string? Overview { get; init; }
    public string? PosterUrl { get; init; }
    public IReadOnlyList<FixtureEpisode> Episodes { get; init; } = [];

    public SeasonDetails ToDetails(ExternalId showId) => new()
    {
        ShowId = showId,
        Number = Number,
        Name = Name,
        Overview = Overview,
        PosterUrl = PosterUrl,
        Episodes = [.. Episodes.Select(e => e.ToDetails())],
    };
}

public sealed record FixtureEpisode
{
    public int Number { get; init; }
    public int? AbsoluteNumber { get; init; }
    public string? Name { get; init; }
    public string? Overview { get; init; }
    public string? AirDate { get; init; }
    public string? StillUrl { get; init; }

    public EpisodeDetails ToDetails() => new(
        Number,
        AbsoluteNumber,
        Name,
        Overview,
        AirDate is null ? null : DateOnly.ParseExact(AirDate, "yyyy-MM-dd", CultureInfo.InvariantCulture),
        StillUrl);
}
