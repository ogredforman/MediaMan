using MediaMan.Core;
using MediaMan.Server.Endpoints;
using MediaMan.Server.Hosting;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MediaMan.Server.LibraryApi;

/// <summary>The detail page's version picker: the session's profile's preferred version per title (SPEC §7.4 step 2, §8 parsing).</summary>
public static class VersionPreferenceEndpoints
{
    public static IEndpointRouteBuilder MapVersionPreferenceEndpoints(this IEndpointRouteBuilder app)
    {
        // A viewer's own choice, like watch progress, so viewing the library is enough.
        app.MapPut("/titles/{titleId}/preferred-version", SetAsync).RequiresPermission(Permissions.LibraryView);
        app.MapDelete("/titles/{titleId}/preferred-version", ClearAsync).RequiresPermission(Permissions.LibraryView);
        return app;
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> SetAsync(
        Guid titleId, PreferredVersionRequest request, HttpContext http, VersionPreferenceService preferences, CancellationToken ct)
    {
        if (ProfileOf(http) is not { } profileId)
        {
            return ProfileRequired();
        }

        return await preferences.SetAsync(profileId, titleId, request.MediaFileId, ct) switch
        {
            null => TypedResults.NoContent(),
            VersionPreferenceFailure.InvalidFile => Problems.Result(StatusCodes.Status400BadRequest, "version.invalid",
                "That version can't be picked.",
                "The file isn't an available version of this title. It may be missing, or a rescan may have moved it.",
                "Reload the title and pick one of the versions it lists."),
            _ => TitleNotFound(),
        };
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> ClearAsync(
        Guid titleId, HttpContext http, VersionPreferenceService preferences, CancellationToken ct)
    {
        if (ProfileOf(http) is not { } profileId)
        {
            return ProfileRequired();
        }

        return await preferences.ClearAsync(profileId, titleId, ct) ? TypedResults.NoContent() : TitleNotFound();
    }

    private static Guid? ProfileOf(HttpContext http) => http.Features.Get<CurrentUser>()?.ProfileId;

    private static ProblemHttpResult ProfileRequired() =>
        Problems.Result(StatusCodes.Status409Conflict, "profile.required", "Pick a profile first.",
            "Version choices are kept per profile, and this session hasn't picked one.",
            "Choose who's watching, then pick the version again.");

    private static ProblemHttpResult TitleNotFound() =>
        Problems.Result(StatusCodes.Status404NotFound, "title.not_found", "Title not found.",
            "This title isn't in the library. It may have been removed or re-matched.",
            "Go back to Movies or Shows and pick it again.");
}
