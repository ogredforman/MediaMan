using System.Text.Json;
using System.Text.Json.Serialization;

namespace MediaMan.Server.LibraryApi;

// Response bodies for the library API; each record is the contract schema of the same name. Enums go out as the
// contract's camelCase strings (see Wire), and absent values are omitted (JsonIgnoreCondition.WhenWritingNull).

public sealed record PageInfo(string? NextCursor);

public sealed record LibraryPage(IReadOnlyList<LibraryDto> Items, PageInfo PageInfo);

public sealed record LibraryDto(
    Guid Id,
    string Name,
    string Kind,
    string ContentType,
    IReadOnlyList<string> RootPaths,
    int ScanIntervalMinutes);

public sealed record ArtworkRef(string Url, string? DominantColor);

public sealed record ExternalIdsDto(
    string? Tmdb,
    string? Imdb,
    string? Tvdb,
    [property: JsonPropertyName("anilist")] string? AniList);

public sealed record TitleSummary(
    Guid Id,
    string Kind,
    string Name,
    int? Year,
    double? Rating,
    IReadOnlyList<string> Genres,
    ArtworkRef? Poster);

public sealed record TitlePage(IReadOnlyList<TitleSummary> Items, PageInfo PageInfo);

public sealed record TitleDetail(
    Guid Id,
    string Kind,
    string Name,
    int? Year,
    double? Rating,
    IReadOnlyList<string> Genres,
    ArtworkRef? Poster,
    string? Overview,
    int? RuntimeMinutes,
    ExternalIdsDto ExternalIds,
    ArtworkRef? Backdrop,
    ArtworkRef? Logo,
    MoviePlayable? Playable,
    IReadOnlyList<SeasonDto>? Seasons);

public sealed record MoviePlayable(Guid Id, IReadOnlyList<MediaVersion> Versions);

public sealed record SeasonDto(
    Guid Id,
    int Number,
    string? Name,
    string? Overview,
    ArtworkRef? Poster,
    IReadOnlyList<EpisodeDto> Episodes);

public sealed record EpisodeDto(
    Guid Id,
    int Number,
    int? AbsoluteNumber,
    string? Name,
    string? Overview,
    DateOnly? AirDate,
    ArtworkRef? Still,
    Guid? PlayableId,
    IReadOnlyList<MediaVersion> Versions);

public sealed record MediaVersion(
    Guid MediaFileId,
    string FileName,
    long SizeBytes,
    string Status,
    int Part,
    string? Container,
    double? DurationSeconds,
    int? Width,
    int? Height,
    string? VideoCodec,
    string? HdrFormat,
    string? AudioCodec,
    int? AudioChannels,
    string? Source,
    string? ReleaseGroup);

/// <summary>Domain enums as the contract's strings: <c>NeedsAttention</c> becomes <c>needsAttention</c>.</summary>
public static class Wire
{
    public static string Name<TEnum>(TEnum value)
        where TEnum : struct, Enum => JsonNamingPolicy.CamelCase.ConvertName(value.ToString());

    public static bool TryParse<TEnum>(string? value, out TEnum result)
        where TEnum : struct, Enum
    {
        foreach (var candidate in Enum.GetValues<TEnum>())
        {
            if (string.Equals(Name(candidate), value, StringComparison.Ordinal))
            {
                result = candidate;
                return true;
            }
        }

        result = default;
        return false;
    }
}
