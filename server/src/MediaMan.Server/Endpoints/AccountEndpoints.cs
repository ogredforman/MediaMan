using MediaMan.Auth;
using MediaMan.Core;
using MediaMan.Data;
using MediaMan.Server.Hosting;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MediaMan.Server.Endpoints;

public sealed record AccountResponse(
    Guid Id, string Username, bool IsSuperadmin, IReadOnlyList<string> Permissions, bool HasPassword, bool PasswordChangeRequired);

public sealed record SetPasswordRequest(string? CurrentPassword, string? NewPassword);

/// <summary>
/// The signed-in account and its permissions, so clients can hide what the user can't use (SPEC §7.11, §11). The
/// server still enforces every permission itself (SPEC §5). Also where an account sets its password (SPEC §7.11).
/// </summary>
public static class AccountEndpoints
{
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/account", GetAsync).RequiresPermission(PermissionMetadata.Authenticated).AllowedDuringPasswordChange();
        app.MapPut("/account/password", SetPasswordAsync).RequiresPermission(PermissionMetadata.Authenticated).AllowedDuringPasswordChange();
        return app;
    }

    private static async Task<Results<Ok<AccountResponse>, ProblemHttpResult>> GetAsync(HttpContext http, MediaManDbContext db, CancellationToken ct)
    {
        var user = http.Features.Get<CurrentUser>() ?? throw new InvalidOperationException("/account needs a signed-in user.");
        var account = await db.Users.AsNoTracking().Where(u => u.Id == user.UserId)
            .Select(u => new { u.Username, u.IsSuperadmin, HasPassword = u.PasswordHash != null })
            .SingleOrDefaultAsync(ct);
        if (account is null)
        {
            // Deleted since the request's permissions were checked.
            return Problems.Result(StatusCodes.Status401Unauthorized, "auth.session_expired", "Your session has ended.",
                "The account no longer exists.", "Sign in again.");
        }

        // Sorted, so the list is stable.
        var permissions = Permissions.All.Order(StringComparer.Ordinal).Where(user.Permissions.Contains).ToList();
        return TypedResults.Ok(new AccountResponse(
            user.UserId, account.Username, account.IsSuperadmin, permissions, account.HasPassword, user.PasswordChangeRequired));
    }

    private static async Task<Results<NoContent, ValidationProblem>> SetPasswordAsync(
        SetPasswordRequest request, HttpContext http, AccountService accounts, CancellationToken ct)
    {
        var user = http.Features.Get<CurrentUser>() ?? throw new InvalidOperationException("/account/password needs a signed-in user.");
        var failure = string.IsNullOrEmpty(request.NewPassword)
            ? new PasswordChangeFailure("newPassword", "Enter a new password.")
            : await accounts.SetPasswordAsync(user.UserId, user.SessionId, request.CurrentPassword, request.NewPassword, ct);
        if (failure is null)
        {
            return TypedResults.NoContent();
        }

        return TypedResults.ValidationProblem(
            new Dictionary<string, string[]> { [failure.Field] = [failure.Message] },
            "The password wasn't changed.",
            title: "Check the password.",
            extensions: new Dictionary<string, object?>
            {
                [Problems.CodeKey] = "account.password_invalid",
                [Problems.ActionKey] = failure.Field == "newPassword"
                    ? "Choose a password that meets the policy, then save."
                    : "Enter your current password, then save.",
            });
    }
}
