using MediaMan.Core;
using MediaMan.Server.Endpoints;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MediaMan.Server.LibraryApi;

/// <summary>The Home screen: the hero pick and its rows for the session's profile (SPEC §8 browsing, §11 Home).</summary>
public static class HomeEndpoints
{
    public static IEndpointRouteBuilder MapHomeEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/home", GetHomeAsync).RequiresPermission(Permissions.LibraryView);
        return app;
    }

    private static async Task<Ok<HomeDto>> GetHomeAsync(HttpContext http, HomeQueries home, CancellationToken ct) =>
        TypedResults.Ok(await home.GetAsync(http.Features.Get<CurrentUser>()?.ProfileId, ct));
}
