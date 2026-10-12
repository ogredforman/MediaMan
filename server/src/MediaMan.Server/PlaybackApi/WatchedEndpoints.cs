using MediaMan.Core;
using MediaMan.Server.Endpoints;
using MediaMan.Server.Hosting;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MediaMan.Server.PlaybackApi;

/// <summary>Mark watched and unwatched for movies, episodes, seasons and whole shows (SPEC §8 browsing).</summary>
public static class WatchedEndpoints
{
    public static IEndpointRouteBuilder MapWatchedEndpoints(this IEndpointRouteBuilder app)
    {
        // The viewer's own watch state, like progress, so viewing the library is enough.
        app.MapPut("/playables/{playableId}/watched", SetPlayableAsync).RequiresPermission(Permissions.LibraryView);
        app.MapPut("/titles/{titleId}/watched", SetTitleAsync).RequiresPermission(Permissions.LibraryView);
        return app;
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> SetPlayableAsync(
        Guid playableId, WatchedRequest? request, HttpContext http, WatchedService watched, CancellationToken ct)
    {
        if (request?.Watched is not { } value)
        {
            return Invalid("The request needs `watched`: true or false.");
        }

        if (ProfileOf(http) is not { } profileId)
        {
            return ProfileRequired();
        }

        return await watched.SetPlayableAsync(profileId, playableId, value, ct)
            ? TypedResults.NoContent()
            : Problems.Result(StatusCodes.Status404NotFound, "playable.not_found", "Nothing to mark here.",
                "This movie or episode doesn't exist any more. A rescan may have removed it.", "Go back to the title and try again.");
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> SetTitleAsync(
        Guid titleId, TitleWatchedRequest? request, HttpContext http, WatchedService watched, CancellationToken ct)
    {
        if (request?.Watched is not { } value)
        {
            return Invalid("The request needs `watched`: true or false.");
        }

        if (request.SeasonNumber is < 0)
        {
            return Invalid("Season numbers start at 0, which holds specials.");
        }

        if (ProfileOf(http) is not { } profileId)
        {
            return ProfileRequired();
        }

        return await watched.SetTitleAsync(profileId, titleId, request.SeasonNumber, value, ct) switch
        {
            null => TypedResults.NoContent(),
            WatchedFailure.SeasonNotFound => Problems.Result(StatusCodes.Status404NotFound, "season.not_found", "Season not found.",
                $"This title has no season {request.SeasonNumber}. A rescan may have changed its seasons.",
                "Reload the title and pick a season it lists."),
            _ => Problems.Result(StatusCodes.Status404NotFound, "title.not_found", "Title not found.",
                "This title isn't in the library. It may have been removed or re-matched.",
                "Go back to Movies or Shows and pick it again."),
        };
    }

    private static Guid? ProfileOf(HttpContext http) => http.Features.Get<CurrentUser>()?.ProfileId;

    private static ProblemHttpResult Invalid(string detail) =>
        Problems.Result(StatusCodes.Status400BadRequest, "watched.invalid", "Watched state not understood.", detail,
            "Update the app, then try again.");

    private static ProblemHttpResult ProfileRequired() =>
        Problems.Result(StatusCodes.Status409Conflict, "profile.required", "Pick a profile first.",
            "Watched markers are kept per profile, and this session hasn't picked one.",
            "Choose who's watching, then mark it again.");
}
