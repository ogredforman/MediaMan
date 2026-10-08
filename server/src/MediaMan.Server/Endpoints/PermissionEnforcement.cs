using MediaMan.Auth;
using MediaMan.Data;
#if MEDIAMAN_DEV
using MediaMan.Server.Dev;
#endif
using MediaMan.Server.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.Net.Http.Headers;

namespace MediaMan.Server.Endpoints;

/// <summary>The signed-in user of a request, set by <see cref="PermissionEnforcement"/>.</summary>
/// <param name="SessionId">The session the access token belongs to; null for the dev configuration's auto sign-in.</param>
public sealed record CurrentUser(Guid UserId, Guid? SessionId, IReadOnlySet<string> Permissions);

/// <summary>
/// Enforces the <see cref="PermissionMetadata"/> every route declares (SPEC §5, §9). Routes other than
/// <see cref="PermissionMetadata.None"/> need a valid access token (checked by JwtBearer) whose session isn't revoked
/// and whose user isn't disabled, and then the route's permission. A route that declares nothing is refused.
/// </summary>
public sealed class PermissionEnforcement(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext http)
    {
        // Framework endpoints (e.g. 405 Method Not Allowed) aren't routes and run nothing of ours.
        if (http.GetEndpoint() is not RouteEndpoint endpoint)
        {
            await next(http);
            return;
        }

        var permission = endpoint.Metadata.GetMetadata<PermissionMetadata>()?.Permission;
        if (permission == PermissionMetadata.None)
        {
            await next(http);
            return;
        }

        if (permission is null)
        {
            await Problems.Result(StatusCodes.Status500InternalServerError, "internal_error", "This request can't be checked.",
                "The route doesn't declare the permission it needs, so the server refuses it.",
                "Report this as a bug in MediaMan.").ExecuteAsync(http);
            return;
        }

        // A signed URL stands in for a token (e.g. artwork in <img>). One that doesn't verify falls through to the token check.
        if (endpoint.Metadata.GetMetadata<SignedUrlMetadata>() is not null
            && http.Request.Query.ContainsKey(UrlSigner.SignatureParameter)
            && http.RequestServices.GetRequiredService<UrlSigner>().IsValid(http.Request.Path.Value ?? "",
                http.Request.Query[UrlSigner.ExpiresParameter].ToString(), http.Request.Query[UrlSigner.SignatureParameter].ToString()))
        {
            await next(http);
            return;
        }

        var (user, failure) = await ResolveAsync(http);
        if (user is null)
        {
            http.Response.Headers[HeaderNames.WWWAuthenticate] = "Bearer";
            await failure!.ExecuteAsync(http);
            return;
        }

        if (permission != PermissionMetadata.Authenticated && !user.Permissions.Contains(permission))
        {
            await Problems.Result(StatusCodes.Status403Forbidden, "auth.forbidden", "You don't have access to this.",
                $"Your account doesn't have the '{permission}' permission this needs.",
                "Ask an admin to give your role this permission.").ExecuteAsync(http);
            return;
        }

        http.Features.Set(user);
        await next(http);
    }

    private static async Task<(CurrentUser? User, IResult? Failure)> ResolveAsync(HttpContext http)
    {
        var db = http.RequestServices.GetRequiredService<MediaManDbContext>();
        var ct = http.RequestAborted;

        if (http.User.Identity?.IsAuthenticated == true)
        {
            var user = Guid.TryParse(http.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var userId)
                && Guid.TryParse(http.User.FindFirst(AccessTokens.SessionIdClaim)?.Value, out var sessionId)
                    ? await LoadAsync(db, userId, sessionId, ct)
                    : null;
            return user is null ? (null, SessionExpired) : (user, null);
        }

#if MEDIAMAN_DEV
        // Dev configuration only (SPEC §8 setup [P]): a request with no token at all is the dev Superadmin.
        if (!http.Request.Headers.ContainsKey(HeaderNames.Authorization)
            && http.RequestServices.GetService<DevAccount>() is { AutoSignIn: true, UserId: { } devUserId })
        {
            var user = await LoadAsync(db, devUserId, sessionId: null, ct);
            return user is null ? (null, SessionExpired) : (user, null);
        }
#endif

        return (null, Problems.Result(StatusCodes.Status401Unauthorized, "auth.required", "Sign in to continue.",
            "This needs a signed-in user, and the request has no valid access token. It may have expired.",
            "Refresh the session, or sign in again."));
    }

    /// <summary>The user if they exist and aren't disabled, and (with a session) the session is theirs and live.</summary>
    private static async Task<CurrentUser?> LoadAsync(MediaManDbContext db, Guid userId, Guid? sessionId, CancellationToken ct)
    {
        if (sessionId is { } sid
            && !await db.Sessions.AnyAsync(s => s.Id == sid && s.UserId == userId && !s.Revoked, ct))
        {
            return null;
        }

        var row = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Join(db.Roles.AsNoTracking(), u => u.RoleId, r => r.Id, (u, r) => new { User = u, Role = r })
            .SingleOrDefaultAsync(ct);
        return row is null || row.User.Disabled
            ? null
            : new CurrentUser(userId, sessionId, AccountService.PermissionsOf(row.User, row.Role));
    }

    // A new result each time: problem details are filled in per request.
    private static IResult SessionExpired => Problems.Result(StatusCodes.Status401Unauthorized, "auth.session_expired",
        "Your session has ended.", "The session was signed out, or the account is disabled.", "Sign in again.");
}
