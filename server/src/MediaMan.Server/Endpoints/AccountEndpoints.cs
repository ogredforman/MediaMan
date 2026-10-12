using MediaMan.Core;
using MediaMan.Data;
using MediaMan.Server.Hosting;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MediaMan.Server.Endpoints;

public sealed record AccountResponse(Guid Id, string Username, bool IsSuperadmin, IReadOnlyList<string> Permissions);

/// <summary>
/// The signed-in account and its permissions, so clients can hide what the user can't use (SPEC §7.11, §11). The
/// server still enforces every permission itself (SPEC §5).
/// </summary>
public static class AccountEndpoints
{
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/account", GetAsync).RequiresPermission(PermissionMetadata.Authenticated);
        return app;
    }

    private static async Task<Results<Ok<AccountResponse>, ProblemHttpResult>> GetAsync(HttpContext http, MediaManDbContext db, CancellationToken ct)
    {
        var user = http.Features.Get<CurrentUser>() ?? throw new InvalidOperationException("/account needs a signed-in user.");
        var account = await db.Users.AsNoTracking().Where(u => u.Id == user.UserId)
            .Select(u => new { u.Username, u.IsSuperadmin })
            .SingleOrDefaultAsync(ct);
        if (account is null)
        {
            // Deleted since the request's permissions were checked.
            return Problems.Result(StatusCodes.Status401Unauthorized, "auth.session_expired", "Your session has ended.",
                "The account no longer exists.", "Sign in again.");
        }

        // Sorted, so the list is stable.
        var permissions = Permissions.All.Order(StringComparer.Ordinal).Where(user.Permissions.Contains).ToList();
        return TypedResults.Ok(new AccountResponse(user.UserId, account.Username, account.IsSuperadmin, permissions));
    }
}
