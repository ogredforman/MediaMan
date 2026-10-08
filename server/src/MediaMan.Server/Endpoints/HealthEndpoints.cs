using System.Reflection;
using MediaMan.Server.Hosting;

namespace MediaMan.Server.Endpoints;

public sealed record HealthResponse(string Status, string Version);

public static class HealthEndpoints
{
    private static readonly string _version =
        typeof(HealthEndpoints).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "0.0.0";

    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/health", () => TypedResults.Ok(new HealthResponse("healthy", _version)))
            .RequiresPermission(PermissionMetadata.None)
            .AllowedBeforeSetup();
        return app;
    }
}
