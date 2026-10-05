using MediaMan.Server.Endpoints;

namespace MediaMan.Server.Events;

public static class EventEndpoints
{
    public static IEndpointRouteBuilder MapEventEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/events", (EventBroadcaster broadcaster) => new EventStreamResult(broadcaster))
            .RequiresPermission(PermissionMetadata.Authenticated);
        return app;
    }
}
