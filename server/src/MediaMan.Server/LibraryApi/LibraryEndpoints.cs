using MediaMan.Core;
using MediaMan.Core.Domain;
using MediaMan.Server.Endpoints;
using MediaMan.Server.Hosting;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MediaMan.Server.LibraryApi;

/// <summary>Library browsing: libraries, the title grid and title detail (SPEC §7.2, §8 browsing).</summary>
public static class LibraryEndpoints
{
    public static IEndpointRouteBuilder MapLibraryEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/libraries", ListLibrariesAsync).RequiresPermission(Permissions.LibraryView);
        app.MapGet("/titles", ListTitlesAsync).RequiresPermission(Permissions.LibraryView);
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

    private static async Task<Results<Ok<TitlePage>, ProblemHttpResult>> ListTitlesAsync(
        string? kind, Guid? libraryId, int? limit, string? cursor, LibraryQueries queries, CancellationToken ct)
    {
        TitleKind? titleKind = null;
        if (kind is not null)
        {
            if (!Wire.TryParse<TitleKind>(kind, out var parsed))
            {
                return Problems.Result(StatusCodes.Status400BadRequest, "titles.invalid_kind", "Unknown title kind.",
                    $"'{kind}' isn't a title kind.", "Use kind=movie or kind=show, or leave it out for both.");
            }

            titleKind = parsed;
        }

        if (!Paging.TryRead(limit, cursor, out var page, out var problem))
        {
            return problem;
        }

        return TypedResults.Ok(await queries.ListTitlesAsync(titleKind, libraryId, page, ct));
    }

    private static async Task<Results<Ok<TitleDetail>, ProblemHttpResult>> GetTitleAsync(
        Guid titleId, LibraryQueries queries, CancellationToken ct)
    {
        var title = await queries.GetTitleAsync(titleId, ct);
        if (title is null)
        {
            return Problems.Result(StatusCodes.Status404NotFound, "title.not_found", "Title not found.",
                "This title isn't in the library. It may have been removed or re-matched.",
                "Go back to Movies or Shows and pick it again.");
        }

        return TypedResults.Ok(title);
    }
}
