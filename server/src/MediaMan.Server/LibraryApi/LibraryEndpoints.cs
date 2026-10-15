using System.Diagnostics.CodeAnalysis;
using MediaMan.Core;
using MediaMan.Core.Domain;
using MediaMan.Server.Endpoints;
using MediaMan.Server.Hosting;
using MediaMan.Server.Scanning;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MediaMan.Server.LibraryApi;

/// <summary>Library management and browsing (libraries, the title grid, genres, search, title detail), scanning and the attention list (SPEC §7.2, §8).</summary>
public static class LibraryEndpoints
{
    // The contract's maxLength on the genre filter.
    private const int MaxGenreLength = 200;

    public static IEndpointRouteBuilder MapLibraryEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/libraries", ListLibrariesAsync).RequiresPermission(Permissions.LibraryView);
        app.MapPost("/libraries", CreateLibraryAsync).RequiresPermission(Permissions.LibraryManage);
        app.MapGet("/libraries/{libraryId}", GetLibraryAsync).RequiresPermission(Permissions.LibraryView);
        app.MapPatch("/libraries/{libraryId}", UpdateLibraryAsync).RequiresPermission(Permissions.LibraryManage);

        // The Admin preset can't delete libraries (SPEC §8 accounts); server.control is what sets it apart.
        app.MapDelete("/libraries/{libraryId}", DeleteLibraryAsync).RequiresPermission(Permissions.ServerControl);
        app.MapPost("/libraries/{libraryId}/scan", StartScanAsync).RequiresPermission(Permissions.LibraryManage);
        app.MapGet("/libraries/{libraryId}/attention", ListAttentionAsync).RequiresPermission(Permissions.LibraryManage);
        app.MapGet("/search", SearchAsync).RequiresPermission(Permissions.LibraryView);
        app.MapGet("/titles", ListTitlesAsync).RequiresPermission(Permissions.LibraryView);
        app.MapGet("/genres", ListGenresAsync).RequiresPermission(Permissions.LibraryView);
        app.MapGet("/titles/{titleId}", GetTitleAsync).RequiresPermission(Permissions.LibraryView);
        return app;
    }

    private static async Task<Results<Ok<LibraryPage>, ProblemHttpResult>> ListLibrariesAsync(
        int? limit, string? cursor, LibraryQueries queries, CancellationToken ct)
    {
        if (!Paging.TryRead(limit, cursor, out var page, out var problem))
        {
            return problem;
        }

        return TypedResults.Ok(await queries.ListLibrariesAsync(page, ct));
    }

    private static async Task<Results<Created<LibraryDto>, ValidationProblem>> CreateLibraryAsync(
        LibraryCreateRequest request, LibraryService libraries, CancellationToken ct)
    {
        var outcome = await libraries.CreateAsync(request, ct);
        return outcome.Library is { } library
            ? TypedResults.Created($"/libraries/{library.Id}", library)
            : Invalid(outcome.Errors!);
    }

    private static async Task<Results<Ok<LibraryDto>, ProblemHttpResult>> GetLibraryAsync(
        Guid libraryId, LibraryQueries queries, CancellationToken ct) =>
        await queries.GetLibraryAsync(libraryId, ct) is { } library ? TypedResults.Ok(library) : LibraryNotFound();

    private static async Task<Results<Ok<LibraryDto>, ValidationProblem, ProblemHttpResult>> UpdateLibraryAsync(
        Guid libraryId, LibraryUpdateRequest request, LibraryService libraries, CancellationToken ct)
    {
        var outcome = await libraries.UpdateAsync(libraryId, request, ct);
        return outcome switch
        {
            { Library: { } library } => TypedResults.Ok(library),
            { Failure: LibraryFailure.Invalid } => Invalid(outcome.Errors!),
            { Failure: LibraryFailure.ScanRunning } => ScanRunning(),
            _ => LibraryNotFound(),
        };
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteLibraryAsync(
        Guid libraryId, LibraryService libraries, CancellationToken ct) =>
        await libraries.DeleteAsync(libraryId, ct) switch
        {
            null => TypedResults.NoContent(),
            LibraryFailure.ScanRunning => ScanRunning(),
            _ => LibraryNotFound(),
        };

    private static ValidationProblem Invalid(IDictionary<string, string[]> errors) =>
        TypedResults.ValidationProblem(errors, "One or more fields are invalid; each one says why.",
            title: "Some library details need fixing.", extensions: new Dictionary<string, object?>
            {
                [Problems.CodeKey] = "library.invalid",
                [Problems.ActionKey] = "Fix the highlighted fields, then save.",
            });

    private static ProblemHttpResult ScanRunning() =>
        Problems.Result(StatusCodes.Status409Conflict, "library.scan_running", "The library is being scanned.",
            "The library's folders can't change, and it can't be removed, while its scan is running.",
            "Wait for the scan to finish, then try again.");

    private static async Task<Results<Accepted<LibraryDto>, ProblemHttpResult>> StartScanAsync(
        Guid libraryId, LibraryQueries queries, ScanCoordinator scans, CancellationToken ct)
    {
        if (await queries.GetLibraryAsync(libraryId, ct) is null)
        {
            return LibraryNotFound();
        }

        if (!scans.IsAvailable)
        {
            return Problems.Result(StatusCodes.Status503ServiceUnavailable, "scan.unavailable", "Scanning isn't available.",
                "No metadata provider or media probe is configured on this server, so files can't be scanned or matched.",
                "Run the server with the dev configuration, or wait for a release with the real providers.");
        }

        scans.Request(libraryId);
        // Read back after queueing, so the body shows the scan as queued or already running.
        var library = await queries.GetLibraryAsync(libraryId, ct);
        return library is null ? LibraryNotFound() : TypedResults.Accepted((string?)null, library);
    }

    private static async Task<Results<Ok<AttentionFilePage>, ProblemHttpResult>> ListAttentionAsync(
        Guid libraryId, int? limit, string? cursor, LibraryQueries queries, CancellationToken ct)
    {
        if (!Paging.TryRead(limit, cursor, out var page, out var problem))
        {
            return problem;
        }

        var files = await queries.ListAttentionAsync(libraryId, page, ct);
        return files is null ? LibraryNotFound() : TypedResults.Ok(files);
    }

    private static ProblemHttpResult LibraryNotFound() =>
        Problems.Result(StatusCodes.Status404NotFound, "library.not_found", "Library not found.",
            "This library doesn't exist. It may have been removed.", "Go back to the library list and pick it again.");

    private static async Task<Results<Ok<LibrarySearchResults>, ProblemHttpResult>> SearchAsync(
        string? q, int? limit, HttpContext http, LibraryQueries queries, CancellationToken ct)
    {
        if (q is null)
        {
            return Problems.Result(StatusCodes.Status400BadRequest, "search.invalid", "No search term.",
                "The search needs a q parameter with what to look for.", "Add ?q= with part of a title's name.");
        }

        if (q.Length > SearchRules.MaxQueryLength)
        {
            return Problems.Result(StatusCodes.Status400BadRequest, "search.invalid", "Search term too long.",
                $"Searches are limited to {SearchRules.MaxQueryLength} characters.", "Shorten the search and try again.");
        }

        if (limit is < 1 or > SearchRules.MaxLimit)
        {
            return Problems.Result(StatusCodes.Status400BadRequest, "search.invalid", "Invalid limit.",
                $"limit must be between 1 and {SearchRules.MaxLimit}.", $"Use a limit in that range, or leave it out for {SearchRules.DefaultLimit}.");
        }

        return TypedResults.Ok(await queries.SearchTitlesAsync(q, limit ?? SearchRules.DefaultLimit, http.Features.Get<CurrentUser>()?.ProfileId, ct));
    }

    private static async Task<Results<Ok<TitlePage>, ProblemHttpResult>> ListTitlesAsync(
        string? kind, Guid? libraryId, string? genre, string? sort, int? limit, string? cursor, HttpContext http, LibraryQueries queries,
        CancellationToken ct)
    {
        if (!TryReadKind(kind, out var titleKind, out var kindProblem))
        {
            return kindProblem;
        }

        if (genre is { Length: > MaxGenreLength })
        {
            return Problems.Result(StatusCodes.Status400BadRequest, "titles.invalid_genre", "Genre too long.",
                $"A genre is at most {MaxGenreLength} characters.", "Pick a genre from the list, or leave it out for every title.");
        }

        var titleSort = TitleSort.Name;
        if (sort is not null && !Wire.TryParse(sort, out titleSort))
        {
            return Problems.Result(StatusCodes.Status400BadRequest, "titles.invalid_sort", "Unknown sort order.",
                $"'{sort}' isn't a sort order.", "Use sort=name or sort=rating, or leave it out to sort by name.");
        }

        if (!Paging.TryRead(limit, cursor, out var page, out var problem))
        {
            return problem;
        }

        return TypedResults.Ok(await queries.ListTitlesAsync(
            titleKind, libraryId, genre, titleSort, page, http.Features.Get<CurrentUser>()?.ProfileId, ct));
    }

    private static async Task<Results<Ok<GenreList>, ProblemHttpResult>> ListGenresAsync(string? kind, LibraryQueries queries, CancellationToken ct)
    {
        if (!TryReadKind(kind, out var titleKind, out var problem))
        {
            return problem;
        }

        return TypedResults.Ok(await queries.ListGenresAsync(titleKind, ct));
    }

    private static bool TryReadKind(string? kind, out TitleKind? titleKind, [NotNullWhen(false)] out ProblemHttpResult? problem)
    {
        titleKind = null;
        problem = null;
        if (kind is null)
        {
            return true;
        }

        if (!Wire.TryParse<TitleKind>(kind, out var parsed))
        {
            problem = Problems.Result(StatusCodes.Status400BadRequest, "titles.invalid_kind", "Unknown title kind.",
                $"'{kind}' isn't a title kind.", "Use kind=movie or kind=show, or leave it out for both.");
            return false;
        }

        titleKind = parsed;
        return true;
    }

    private static async Task<Results<Ok<TitleDetail>, ProblemHttpResult>> GetTitleAsync(
        Guid titleId, HttpContext http, LibraryQueries queries, CancellationToken ct)
    {
        var title = await queries.GetTitleAsync(titleId, http.Features.Get<CurrentUser>()?.ProfileId, ct);
        if (title is null)
        {
            return Problems.Result(StatusCodes.Status404NotFound, "title.not_found", "Title not found.",
                "This title isn't in the library. It may have been removed or re-matched.",
                "Go back to Movies or Shows and pick it again.");
        }

        return TypedResults.Ok(title);
    }
}
