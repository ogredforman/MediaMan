using MediaMan.Auth;
using MediaMan.Server.Hosting;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Net.Http.Headers;

namespace MediaMan.Server.Endpoints;

public sealed record LoginRequest(string? Username, string? Password, string? DeviceName);

public sealed record RefreshRequest(string? RefreshToken);

/// <summary>
/// Sign-in, token refresh and sign-out (SPEC §7.11). Signing in and refreshing are unauthenticated: refreshing is part of
/// signing in.
/// </summary>
public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/auth/login", LoginAsync).RequiresPermission(PermissionMetadata.None);
        app.MapPost("/auth/refresh", RefreshAsync).RequiresPermission(PermissionMetadata.None);
        app.MapPost("/auth/logout", LogoutAsync).RequiresPermission(PermissionMetadata.Authenticated);
        return app;
    }

    private static async Task<Results<Ok<AuthTokens>, ProblemHttpResult>> LoginAsync(
        LoginRequest request, HttpContext http, SessionService sessions, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrEmpty(request.Password))
        {
            return Problems.Result(StatusCodes.Status400BadRequest, "bad_request", "Username and password are required.",
                "The sign-in request is missing the username or the password.", "Enter both, then sign in again.");
        }

        var deviceName = request.DeviceName ?? http.Request.Headers[HeaderNames.UserAgent].ToString();
        var result = await sessions.SignInAsync(request.Username, request.Password, deviceName, ct);
        return result.Tokens is { } tokens
            ? TypedResults.Ok(tokens)
            : Problems.Result(StatusCodes.Status401Unauthorized, "auth.invalid_credentials", "Couldn't sign in.",
                "The username or password is wrong, or the account is disabled.",
                "Check the username and password and try again. If the account is disabled, ask an admin.");
    }

    private static async Task<Results<Ok<AuthTokens>, ProblemHttpResult>> RefreshAsync(
        RefreshRequest request, SessionService sessions, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(request.RefreshToken))
        {
            return Problems.Result(StatusCodes.Status400BadRequest, "bad_request", "A refresh token is required.",
                "The refresh request has no refresh token.", "Sign in again.");
        }

        var result = await sessions.RefreshAsync(request.RefreshToken, ct);
        return result.Tokens is { } tokens
            ? TypedResults.Ok(tokens)
            : Problems.Result(StatusCodes.Status401Unauthorized, "auth.session_expired", "Your session has ended.",
                "The session expired or was signed out, or the account is disabled.", "Sign in again.");
    }

    /// <summary>
    /// Revokes the token's session. A request without a token is the dev configuration's auto sign-in, whose session
    /// stays: there's no sign-in to go back to.
    /// </summary>
    private static async Task<NoContent> LogoutAsync(HttpContext http, SessionService sessions, CancellationToken ct)
    {
        if (http.Request.Headers.ContainsKey(HeaderNames.Authorization)
            && http.Features.Get<CurrentUser>()?.SessionId is { } sessionId)
        {
            await sessions.RevokeAsync(sessionId, ct);
        }

        return TypedResults.NoContent();
    }
}
