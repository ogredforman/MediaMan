using MediaMan.Core;
using MediaMan.Data;
using MediaMan.Server.Endpoints;
using MediaMan.Server.Hosting;
using MediaMan.Server.PlaybackApi;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MediaMan.Server.Dev;

/// <summary>Body of a successful dev play: the file MPV was launched with.</summary>
public sealed record DevPlayStarted(Guid MediaFileId, string FileName);

/// <summary>
/// The dev-only play endpoint (SPEC §8 playback [P], §13): launches MPV on the server's host. Mapped only in the dev
/// configuration of a Debug build, so it returns 404 everywhere else and never ships.
/// </summary>
public static class DevPlayEndpoints
{
    public static IEndpointRouteBuilder MapDevPlayEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/dev/playables/{playableId}/play", PlayAsync).RequiresPermission(Permissions.LibraryView);
        return app;
    }

    private static async Task<Results<Accepted<DevPlayStarted>, ProblemHttpResult>> PlayAsync(
        Guid playableId, HttpContext http, MediaManDbContext db, PlaybackService playback, IDevPlayer player,
        CancellationToken ct)
    {
        var playable = await db.Playables.AsNoTracking().SingleOrDefaultAsync(p => p.Id == playableId, ct);
        if (playable is null)
        {
            return Problems.Result(StatusCodes.Status404NotFound, "playable.not_found", "Nothing to play here.",
                "This movie or episode doesn't exist any more. A rescan may have removed it.", "Go back to the title and try again.");
        }

        if (await playback.PickFileAsync(playableId, http.Features.Get<CurrentUser>()?.ProfileId, ct) is not { } file)
        {
            return Problems.Result(StatusCodes.Status409Conflict, "playback.no_file", "No file to play.",
                "None of this title's files can be found on disk.", "Reconnect the drive that holds it, then scan the library again.");
        }

        try
        {
            await player.PlayAsync(file.Path, await playback.DisplayNameAsync(playable, ct), ct);
        }
        catch (DevPlayerException e)
        {
            return Problems.Result(StatusCodes.Status503ServiceUnavailable, "playback.player_unavailable", "MPV couldn't start.",
                e.Message, $"Install MPV, or set {MediaManOptions.SectionName}:Tools:MpvPath to the MPV executable, then try again.");
        }

        return TypedResults.Accepted((string?)null, new DevPlayStarted(file.Id, Path.GetFileName(file.Path)));
    }
}
