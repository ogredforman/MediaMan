using MediaMan.Core;
using MediaMan.Core.Domain;
using MediaMan.Core.Modules;
using MediaMan.Library.Matching;
using MediaMan.Server.Endpoints;
using MediaMan.Server.Hosting;
using MediaMan.Server.Scanning;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MediaMan.Server.LibraryApi;

public sealed record CatalogSearchResult(IReadOnlyList<CatalogCandidate> Items);

public sealed record CatalogCandidate(string TitleRef, string Kind, string Name, int? Year, string? Overview, string? PosterUrl);

public sealed record MediaFileMatchRequest(string? TitleRef, int? Season, IReadOnlyList<int>? Episodes);

public sealed record MediaFileMatch(Guid MediaFileId, Guid TitleId, string Status);

/// <summary>"Fix match": the manual catalog search and relinking a file (SPEC §7.3); the attention list is in <see cref="LibraryEndpoints"/>.</summary>
public static class MatchEndpoints
{
    public const int MaxQueryLength = 200;

    public static IEndpointRouteBuilder MapMatchEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/catalog/search", SearchCatalogAsync).RequiresPermission(Permissions.LibraryManage);
        app.MapPut("/media-files/{mediaFileId}/match", RelinkAsync).RequiresPermission(Permissions.LibraryManage);
        return app;
    }

    private static async Task<Results<Ok<CatalogSearchResult>, ProblemHttpResult>> SearchCatalogAsync(
        string? query, int? year, string? kind, MatchFixer fixer, CancellationToken ct)
    {
        var name = query?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > MaxQueryLength)
        {
            return CatalogInvalid($"Enter a title to search for, up to {MaxQueryLength} characters.");
        }

        if (year is < 1870 or > 2200)
        {
            return CatalogInvalid($"{year} isn't a release year.");
        }

        TitleKind? titleKind = null;
        if (kind is not null)
        {
            if (!Wire.TryParse<TitleKind>(kind, out var parsed))
            {
                return CatalogInvalid($"'{kind}' isn't a title kind; use movie or show.");
            }

            titleKind = parsed;
        }

        var results = await fixer.SearchAsync(new TitleQuery(name, year, titleKind), ct);
        return results is null
            ? MetadataUnavailable()
            : TypedResults.Ok(new CatalogSearchResult(
                [.. results.Select(c => new CatalogCandidate(c.Id.ToString(), Wire.Name(c.Kind), c.Name, c.Year, c.Overview, c.PosterUrl))]));
    }

    private static async Task<Results<Ok<MediaFileMatch>, ValidationProblem, ProblemHttpResult>> RelinkAsync(
        Guid mediaFileId, MediaFileMatchRequest request, MatchFixer fixer, ScanCoordinator scans, CancellationToken ct)
    {
        var outcome = await fixer.RelinkAsync(
            mediaFileId,
            new MatchChoice(request.TitleRef, request.Season, request.Episodes),
            libraryId => scans.Get(libraryId).State == Wire.Name(ScanStatus.Running),
            ct);
        return outcome switch
        {
            { TitleId: { } titleId } => TypedResults.Ok(new MediaFileMatch(mediaFileId, titleId, Wire.Name(MediaFileStatus.Matched))),
            { Failure: RelinkFailure.Invalid } => TypedResults.ValidationProblem(outcome.Errors!,
                "One or more fields are invalid; each one says why.", title: "This match needs fixing.",
                extensions: new Dictionary<string, object?>
                {
                    [Problems.CodeKey] = "match.invalid",
                    [Problems.ActionKey] = "Fix the highlighted fields, then save.",
                }),
            { Failure: RelinkFailure.FileUnavailable } => Problems.Result(StatusCodes.Status409Conflict, "match.file_unavailable",
                "This file can't be linked.", "The file is missing, ignored or can't be read, so there's nothing to link.",
                "Check the file is in its library folder and plays, then scan the library again."),
            { Failure: RelinkFailure.ScanRunning } => Problems.Result(StatusCodes.Status409Conflict, "library.scan_running",
                "The library is being scanned.", "Files can't be relinked while their library's scan is running.",
                "Wait for the scan to finish, then try again."),
            { Failure: RelinkFailure.EpisodeNotFound } => Problems.Result(StatusCodes.Status422UnprocessableEntity,
                "match.episode_not_found", "That episode isn't in the catalog.",
                "The metadata provider doesn't list the chosen season or episode for this show.",
                "Check the season and episode numbers, or pick another show."),
            { Failure: RelinkFailure.ProviderUnavailable } => MetadataUnavailable(),
            _ => Problems.Result(StatusCodes.Status404NotFound, "media_file.not_found", "File not found.",
                "This file isn't in any library. It may have been removed by a scan.", "Reload the list and pick the file again."),
        };
    }

    private static ProblemHttpResult CatalogInvalid(string detail) =>
        Problems.Result(StatusCodes.Status400BadRequest, "catalog.invalid", "The search can't run.", detail, "Change the search and try again.");

    private static ProblemHttpResult MetadataUnavailable() =>
        Problems.Result(StatusCodes.Status503ServiceUnavailable, "metadata.unavailable", "The metadata provider didn't answer.",
            "The catalog couldn't be reached, so nothing changed.", "Try again in a moment.");
}
