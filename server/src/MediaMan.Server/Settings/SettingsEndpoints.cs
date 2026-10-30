using MediaMan.Auth;
using MediaMan.Core;
using MediaMan.Data;
using MediaMan.Server.Endpoints;
using MediaMan.Server.Hosting;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MediaMan.Server.Settings;

public sealed record PasswordPolicyRequest(bool? RequirePasswords, bool? StrongPasswords);

/// <summary>Settings that live on the server (SPEC §11 Settings).</summary>
public static class SettingsEndpoints
{
    public static IEndpointRouteBuilder MapSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/settings/metadata", (MetadataSettingsService settings) => TypedResults.Ok(settings.Get()))
            .RequiresPermission(Permissions.SettingsManage);
        app.MapPut("/settings/metadata", SetMetadataAsync).RequiresPermission(Permissions.SettingsManage);
        app.MapGet("/settings/password-policy", async (MediaManDbContext db, CancellationToken ct) =>
                TypedResults.Ok(await PasswordPolicy.LoadAsync(db, ct)))
            .RequiresPermission(PermissionMetadata.Superadmin);
        app.MapPut("/settings/password-policy", SetPasswordPolicyAsync).RequiresPermission(PermissionMetadata.Superadmin);
        return app;
    }

    private static async Task<Results<Ok<PasswordPolicy>, ValidationProblem>> SetPasswordPolicyAsync(
        PasswordPolicyRequest request, MediaManDbContext db, CancellationToken ct)
    {
        if (request is not { RequirePasswords: { } required, StrongPasswords: { } strong })
        {
            var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
            if (request.RequirePasswords is null)
            {
                errors["requirePasswords"] = ["Say whether accounts need a password."];
            }

            if (request.StrongPasswords is null)
            {
                errors["strongPasswords"] = ["Say whether passwords must be strong."];
            }

            return TypedResults.ValidationProblem(errors, "The password policy is incomplete.", title: "Check the password policy.",
                extensions: new Dictionary<string, object?>
                {
                    [Problems.CodeKey] = "bad_request",
                    [Problems.ActionKey] = "Choose both options, then save.",
                });
        }

        // Remote access can't be turned on yet; 6.1 refuses turning passwords off while it is (SPEC §7.12).
        var policy = new PasswordPolicy(required, strong);
        await PasswordPolicy.SaveAsync(db, policy, ct);
        return TypedResults.Ok(policy);
    }

    private static async Task<Results<Ok<MetadataSettings>, ValidationProblem>> SetMetadataAsync(
        MetadataSettingsRequest request, MetadataSettingsService settings, CancellationToken ct)
    {
        var error = request.TmdbAccessToken is null
            ? "Paste a TMDB read-access token, or send an empty value to use the default."
            : MetadataSettingsService.Validate(request.TmdbAccessToken);
        if (error is not null)
        {
            return TypedResults.ValidationProblem(
                new Dictionary<string, string[]> { ["tmdbAccessToken"] = [error] },
                "The TMDB access token can't be used.",
                title: "Check the TMDB access token.",
                extensions: new Dictionary<string, object?>
                {
                    [Problems.CodeKey] = "bad_request",
                    [Problems.ActionKey] = "Paste the API Read Access Token from your TMDB account, then save.",
                });
        }

        return TypedResults.Ok(await settings.SetAsync(request.TmdbAccessToken, ct));
    }
}
