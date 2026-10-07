using MediaMan.Core;
using MediaMan.Core.Domain;
using MediaMan.Data;
using MediaMan.Server.Endpoints;
using MediaMan.Server.Hosting;
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

    /// <summary>The best version to play: present on disk and not Missing, then the highest resolution, then the largest.</summary>
    public static MediaFile? PickVersion(IEnumerable<MediaFile> files) =>
        files
            .Where(f => f.Status != MediaFileStatus.Missing && File.Exists(f.Path))
            .OrderByDescending(f => f.Probe?.Streams.Where(s => s.Type == MediaStreamType.Video).Max(s => s.Height) ?? 0)
            .ThenByDescending(f => f.SizeBytes)
            .ThenBy(f => f.Path, StringComparer.Ordinal)
            .FirstOrDefault();

    private static async Task<Results<Accepted<DevPlayStarted>, ProblemHttpResult>> PlayAsync(
        Guid playableId, MediaManDbContext db, IDevPlayer player, CancellationToken ct)
    {
        var playable = await db.Playables.AsNoTracking().SingleOrDefaultAsync(p => p.Id == playableId, ct);
        if (playable is null)
        {
            return Problems.Result(StatusCodes.Status404NotFound, "playable.not_found", "Nothing to play here.",
                "This movie or episode doesn't exist any more. A rescan may have removed it.", "Go back to the title and try again.");
        }

        var files = await db.FileLinks.AsNoTracking()
            .Where(l => l.PlayableId == playableId)
            .Join(db.MediaFiles.AsNoTracking(), l => l.MediaFileId, f => f.Id, (_, f) => f)
            .ToListAsync(ct);
        if (PickVersion(files) is not { } file)
        {
            return Problems.Result(StatusCodes.Status409Conflict, "playback.no_file", "No file to play.",
                "None of this title's files can be found on disk.", "Reconnect the drive that holds it, then scan the library again.");
        }

        try
        {
            await player.PlayAsync(file.Path, await DisplayNameAsync(db, playable, ct), ct);
        }
        catch (DevPlayerException e)
        {
            return Problems.Result(StatusCodes.Status503ServiceUnavailable, "playback.player_unavailable", "MPV couldn't start.",
                e.Message, $"Install MPV, or set {MediaManOptions.SectionName}:Tools:MpvPath to the MPV executable, then try again.");
        }

        return TypedResults.Accepted((string?)null, new DevPlayStarted(file.Id, Path.GetFileName(file.Path)));
    }

    /// <summary>The MPV window title: "Name (Year)" for a movie, "Show S01E02 · Episode name" for an episode.</summary>
    private static async Task<string> DisplayNameAsync(MediaManDbContext db, Playable playable, CancellationToken ct)
    {
        if (playable.TitleId is { } titleId)
        {
            var title = await db.Titles.AsNoTracking().SingleAsync(t => t.Id == titleId, ct);
            return title.Year is { } year ? $"{title.Name} ({year})" : title.Name;
        }

        var episode = await db.Episodes.AsNoTracking().SingleAsync(e => e.Id == playable.EpisodeId, ct);
        var season = await db.Seasons.AsNoTracking().SingleAsync(s => s.Id == episode.SeasonId, ct);
        var show = await db.Titles.AsNoTracking().SingleAsync(t => t.Id == season.TitleId, ct);
        var code = $"{show.Name} S{season.Number:00}E{episode.Number:00}";
        return episode.Name is null ? code : $"{code} · {episode.Name}";
    }
}
