using MediaMan.Auth;
using MediaMan.Core.Domain;
using MediaMan.Server.Hosting;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MediaMan.Server.Endpoints;

// Request and response bodies; each record is the contract schema of the same name.

public sealed record ProfileDto(Guid Id, string Name, string Avatar);

public sealed record ProfileList(IReadOnlyList<ProfileDto> Items, Guid? ActiveProfileId);

public sealed record CreateProfileRequest(string? Name, string? Avatar);

public sealed record UpdateProfileRequest(string? Name, string? Avatar);

public sealed record SelectProfileRequest(Guid? ProfileId);

/// <summary>
/// The signed-in account's profiles and the session's pick (SPEC §7.11 step 3, §8 accounts). Any signed-in user manages
/// their own profiles; another account's profile is a 404.
/// </summary>
public static class ProfileEndpoints
{
    public static IEndpointRouteBuilder MapProfileEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/profiles", ListAsync).RequiresPermission(PermissionMetadata.Authenticated);
        app.MapPost("/profiles", CreateAsync).RequiresPermission(PermissionMetadata.Authenticated);
        app.MapPatch("/profiles/{profileId}", UpdateAsync).RequiresPermission(PermissionMetadata.Authenticated);
        app.MapDelete("/profiles/{profileId}", DeleteAsync).RequiresPermission(PermissionMetadata.Authenticated);
        app.MapPut("/auth/session/profile", SelectAsync).RequiresPermission(PermissionMetadata.Authenticated);
        return app;
    }

    private static async Task<Ok<ProfileList>> ListAsync(HttpContext http, ProfileService profiles, CancellationToken ct)
    {
        var user = CurrentUserOf(http);
        var items = await profiles.ListAsync(user.UserId, ct);
        return TypedResults.Ok(new ProfileList([.. items.Select(ToDto)], user.ProfileId));
    }

    private static async Task<Results<Created<ProfileDto>, ValidationProblem, ProblemHttpResult>> CreateAsync(
        CreateProfileRequest request, HttpContext http, ProfileService profiles, CancellationToken ct)
    {
        var result = await profiles.CreateAsync(CurrentUserOf(http).UserId, request.Name, request.Avatar, ct);
        return result.Profile is { } profile
            ? TypedResults.Created($"/profiles/{profile.Id}", ToDto(profile))
            : Failure<Created<ProfileDto>>(result);
    }

    private static async Task<Results<Ok<ProfileDto>, ValidationProblem, ProblemHttpResult>> UpdateAsync(
        Guid profileId, UpdateProfileRequest request, HttpContext http, ProfileService profiles, CancellationToken ct)
    {
        var result = await profiles.UpdateAsync(CurrentUserOf(http).UserId, profileId, request.Name, request.Avatar, ct);
        return result.Profile is { } profile ? TypedResults.Ok(ToDto(profile)) : Failure<Ok<ProfileDto>>(result);
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(
        Guid profileId, HttpContext http, ProfileService profiles, CancellationToken ct)
    {
        return await profiles.DeleteAsync(CurrentUserOf(http).UserId, profileId, ct) switch
        {
            null => TypedResults.NoContent(),
            ProfileFailure.Last => Problems.Result(StatusCodes.Status409Conflict, "profile.last", "This is the only profile.",
                "An account always keeps at least one profile, so its last one can't be deleted.",
                "Add another profile first, or rename this one instead."),
            _ => NotFound,
        };
    }

    private static async Task<Results<Ok<ProfileDto>, ProblemHttpResult>> SelectAsync(
        SelectProfileRequest request, HttpContext http, ProfileService profiles, CancellationToken ct)
    {
        var user = CurrentUserOf(http);
        if (request.ProfileId is not { } profileId)
        {
            return Problems.Result(StatusCodes.Status400BadRequest, "bad_request", "A profile is required.",
                "The request doesn't say which profile to pick.", "Pick a profile, then try again.");
        }

        if (user.SessionId is not { } sessionId)
        {
            // Only a token without a session gets here, and access tokens always carry one.
            return Problems.Result(StatusCodes.Status401Unauthorized, "auth.session_expired", "Your session has ended.",
                "The request isn't tied to a signed-in session.", "Sign in again.");
        }

        var result = await profiles.SelectAsync(user.UserId, sessionId, profileId, ct);
        return result.Profile is { } profile ? TypedResults.Ok(ToDto(profile)) : NotFound;
    }

    private static CurrentUser CurrentUserOf(HttpContext http) =>
        http.Features.Get<CurrentUser>() ?? throw new InvalidOperationException("Profile routes need a signed-in user.");

    private static ProfileDto ToDto(Profile profile) =>
        new(profile.Id, profile.Name, profile.Avatar ?? ProfileService.Avatars[0]);

    private static ProblemHttpResult NotFound => Problems.Result(StatusCodes.Status404NotFound, "profile.not_found",
        "Profile not found.", "The profile doesn't exist, or it was deleted.", "Reload the profile list.");

    private static Results<T, ValidationProblem, ProblemHttpResult> Failure<T>(ProfileResult result)
        where T : IResult
    {
        return result.Failure switch
        {
            ProfileFailure.Invalid => TypedResults.ValidationProblem(
                result.Errors!.ToDictionary(e => e.Key, e => e.Value), "One or more fields are invalid; each one says why.",
                title: "Some profile details need fixing.", extensions: new Dictionary<string, object?>
                {
                    [Problems.CodeKey] = "profile.invalid",
                    [Problems.ActionKey] = "Fix the highlighted fields, then save again.",
                }),
            ProfileFailure.NameTaken => Problems.Result(StatusCodes.Status409Conflict, "profile.name_taken",
                "That name is taken.", "Another profile on this account already has that name.", "Choose a different name."),
            ProfileFailure.Limit => Problems.Result(StatusCodes.Status409Conflict, "profile.limit", "No room for another profile.",
                $"An account can have at most {ProfileService.MaximumProfiles} profiles.", "Delete a profile you don't use, then add this one."),
            _ => NotFound,
        };
    }
}
