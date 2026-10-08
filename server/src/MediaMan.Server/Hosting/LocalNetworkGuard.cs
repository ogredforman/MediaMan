using Microsoft.Extensions.Primitives;

namespace MediaMan.Server.Hosting;

/// <summary>
/// Refuses clients outside the local network while remote access is off (SPEC §5 private until opened, §7.12 step 1).
/// Runs before authentication, so it covers every request, health and setup included. A request carrying forwarding
/// headers counts as outside, so a tunnel or proxy on the same machine can't expose the server by accident; so does a
/// request whose client address is unknown. Remote access and trusted proxies (Stage 6) are added here.
/// </summary>
public sealed class LocalNetworkGuard(RequestDelegate next)
{
    /// <summary>Headers a proxy or tunnel adds; any of them means the request came through one (SPEC §7.12 step 1).</summary>
    public static readonly IReadOnlyList<string> ForwardingHeaders = ["Forwarded", "X-Forwarded-For", "X-Real-IP", "CF-Connecting-IP"];

    public async Task InvokeAsync(HttpContext http)
    {
        if (IsLocalRequest(http))
        {
            await next(http);
            return;
        }

        await Problems.Result(StatusCodes.Status403Forbidden, "remote_access.disabled", "Remote access is off.",
            "MediaMan only serves this computer and devices on your local network or tailnet until remote access is " +
            "turned on, and this request came from outside it or through a proxy or tunnel.",
            "Connect from a device on your local network or over Tailscale, or ask the owner to turn on remote access " +
            "in Settings → Remote access.").ExecuteAsync(http);
    }

    private static bool IsLocalRequest(HttpContext http) =>
        http.Connection.RemoteIpAddress is { } address
        && LocalNetwork.IsLocal(address)
        && !ForwardingHeaders.Any(h => http.Request.Headers.TryGetValue(h, out StringValues value) && value.Count > 0);
}
