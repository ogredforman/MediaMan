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
    int ScanIntervalMinutes,
    ScanState Scan);

public sealed record ScanState(string State, string? Phase, DateTimeOffset? StartedAt, ScanSummary? LastScan);

public sealed record ScanSummary(DateTimeOffset StartedAt, DateTimeOffset FinishedAt, string Outcome, ScanCounts Counts, ScanError? Error);

public sealed record ScanCounts(
    int Added,
    int Changed,
    int Unchanged,
    int Restored,
    int Missing,
    int Unreadable,
    int Purged,
    int Linked,
    int NeedsAttention,
    int Deferred);

public sealed record ScanError(string Code, string Detail);

public sealed record ScanProgressEvent(Guid LibraryId, ScanState Scan);

public sealed record AttentionFilePage(IReadOnlyList<AttentionFile> Items, PageInfo PageInfo);

public sealed record AttentionFile(Guid MediaFileId, string Path, string FileName, string Reason, ParsedGuess? Parsed, MatchReviewDto? Review);

public sealed record ParsedGuess(
    string? Title,
    int? Year,
    int? Season,
    IReadOnlyList<int> Episodes,
    int? AbsoluteEpisode,
    string? Resolution,
    string? Source,
    string? Codec,
    string? HdrFormat,
    string? Audio,
    string? ReleaseGroup);

public sealed record MatchReviewDto(Guid Id, string Reason, IReadOnlyList<MatchCandidateDto> Candidates);

public sealed record MatchCandidateDto(string TitleRef, double Score, string? Kind, string? Name, int? Year, string? PosterUrl);

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
    IReadOnlyList<SeasonDto>? Seasons,
    VersionPreferenceDto? VersionPreference);

public sealed record VersionPreferenceDto(Guid? MediaFileId, string? Resolution, string? HdrFormat);

public sealed record PreferredVersionRequest(Guid MediaFileId);

public sealed record MoviePlayable(
    Guid Id, IReadOnlyList<MediaVersion> Versions, Guid? SelectedMediaFileId, double? ResumeSeconds, bool Watched);

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
    IReadOnlyList<MediaVersion> Versions,
    Guid? SelectedMediaFileId,
    double? ResumeSeconds,
    bool Watched);

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
