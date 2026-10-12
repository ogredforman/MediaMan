using MediaMan.Auth;
using MediaMan.Core;
using MediaMan.Data;
using MediaMan.Server.Endpoints;
using MediaMan.Server.Hosting;
using MediaMan.Server.LibraryApi;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Net.Http.Headers;

namespace MediaMan.Server.Setup;

public sealed record ServerModeRequest(string? Mode);

public sealed record ServerModeState(string Mode, bool RestartRequired);

/// <summary>First-run setup (SPEC §7.1) and changing the mode afterwards (SPEC §8 setup [M]).</summary>
public static class SetupEndpoints
{
    public static IEndpointRouteBuilder MapSetupEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/setup", CompleteAsync).RequiresPermission(PermissionMetadata.None).AllowedBeforeSetup();
        app.MapGet("/server/mode", GetModeAsync).RequiresPermission(Permissions.ServerControl);
        app.MapPut("/server/mode", SetModeAsync).RequiresPermission(Permissions.ServerControl);
        return app;
    }

    private static async Task<Ok<ServerModeState>> GetModeAsync(MediaManDbContext db, ServerModes modes, CancellationToken ct)
    {
        var mode = await ServerModes.RecordedAsync(db, ct) ?? modes.Active;
        return TypedResults.Ok(new ServerModeState(Wire.Name(mode), mode != modes.Active));
    }

    private static async Task<Results<Ok<AuthTokens>, ValidationProblem, ProblemHttpResult>> CompleteAsync(
        SetupRequest request, HttpContext http, SetupService setup, CancellationToken ct)
    {
        var outcome = await setup.CompleteAsync(request, http.Request.Headers[HeaderNames.UserAgent].ToString(), ct);
        return outcome switch
        {
            { Tokens: { } tokens } => TypedResults.Ok(tokens),
            { Failure: SetupFailure.Invalid } => Invalid("setup.invalid", "Some setup details need fixing.",
                "One or more fields are invalid; each one says why.", "Fix the highlighted fields, then finish setup.", outcome.Errors!),
            _ => Problems.Result(StatusCodes.Status409Conflict, "setup.complete", "MediaMan is already set up.",
                "This server already has an owner account, so setup can't run again.", "Sign in instead."),
        };
    }

    private static async Task<Results<Ok<ServerModeState>, ValidationProblem>> SetModeAsync(
        ServerModeRequest request, MediaManDbContext db, ServerModes modes, CancellationToken ct)
    {
        if (!Wire.TryParse<ServerMode>(request.Mode, out var mode))
        {
            return Invalid("bad_request", "Choose a mode.", "The mode is missing or unknown.", "Choose 'local' or 'server', then save.",
                new Dictionary<string, string[]> { ["mode"] = ["Choose 'local' (this computer only) or 'server' (the local network)."] });
        }

        await ServerModes.RecordAsync(db, mode, ct);
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(new ServerModeState(Wire.Name(mode), mode != modes.Active));
    }

    private static ValidationProblem Invalid(string code, string title, string detail, string action, IDictionary<string, string[]> errors) =>
        TypedResults.ValidationProblem(errors, detail, title: title, extensions: new Dictionary<string, object?>
        {
            [Problems.CodeKey] = code,
            [Problems.ActionKey] = action,
        });
}
