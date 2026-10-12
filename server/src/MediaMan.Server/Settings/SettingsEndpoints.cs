using MediaMan.Core;
using MediaMan.Server.Endpoints;
using MediaMan.Server.Hosting;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MediaMan.Server.Settings;

/// <summary>Settings that live on the server (SPEC §11 Settings).</summary>
public static class SettingsEndpoints
{
    public static IEndpointRouteBuilder MapSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/settings/metadata", (MetadataSettingsService settings) => TypedResults.Ok(settings.Get()))
            .RequiresPermission(Permissions.SettingsManage);
        app.MapPut("/settings/metadata", SetMetadataAsync).RequiresPermission(Permissions.SettingsManage);
        return app;
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
