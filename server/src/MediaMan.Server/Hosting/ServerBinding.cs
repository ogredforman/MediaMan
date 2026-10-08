using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Options;

namespace MediaMan.Server.Hosting;

/// <summary>Where Local and Server mode listen (SPEC §7.1 step 3, §8 setup [M]).</summary>
public enum ServerMode
{
    /// <summary>This computer only: loopback.</summary>
    Local,

    /// <summary>The local network: every interface, with <see cref="LocalNetworkGuard"/> refusing outside clients.</summary>
    Server,
}

/// <summary>The addresses the server listens on, resolved from the mode.</summary>
/// <param name="Loopback">True to listen on loopback only; false for every interface.</param>
public sealed record ListenPlan(bool Loopback, int Port, string Scheme);

/// <summary>
/// The one place that decides the listen addresses and scheme, and puts the local-network guard in front of
/// everything (PLAN 2.4). Kestrel listens only where this says, so <c>ASPNETCORE_URLS</c> and <c>--urls</c> are
/// ignored. Stage 6 adds HTTPS and trusted proxies here.
/// </summary>
public static class ServerBinding
{
    public static ListenPlan Plan(ServerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.Port is < 1 or > 65535)
        {
            throw new InvalidOperationException(
                $"{MediaManOptions.SectionName}:Server:Port is {options.Port}; set it to a port from 1 to 65535.");
        }

        return new ListenPlan(options.Mode == ServerMode.Local, options.Port, Uri.UriSchemeHttp);
    }

    public static IServiceCollection AddMediaManBinding(this IServiceCollection services)
    {
        services.AddOptions<KestrelServerOptions>().Configure<IOptions<MediaManOptions>>((kestrel, options) =>
        {
            var plan = Plan(options.Value.Server);
            if (plan.Loopback)
            {
                kestrel.ListenLocalhost(plan.Port); // IPv4 and IPv6 loopback, whichever the machine has
            }
            else
            {
                kestrel.ListenAnyIP(plan.Port);
            }
        });
        return services;
    }

    /// <summary>Refuses clients outside the local network before anything else of ours runs (SPEC §7.12 step 1).</summary>
    public static WebApplication UseLocalNetworkGuard(this WebApplication app)
    {
        app.UseMiddleware<LocalNetworkGuard>();
        return app;
    }
}
