using MediaMan.Core;
using MediaMan.Server.Endpoints;
using MediaMan.Server.Hosting;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MediaMan.Server.PlaybackApi;

/// <summary>
/// Playback through the client-capability negotiation (SPEC §4, §7.4, §7.5): <c>POST /playback/sessions</c>, and the
/// stream and subtitle files its session-signed URLs point at.
/// </summary>
public static class PlaybackEndpoints
{
    public static IEndpointRouteBuilder MapPlaybackEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/playback/sessions", CreateSessionAsync).RequiresPermission(Permissions.LibraryView);
        // Players load these without a token, so the session's signed URL stands in for it.
        app.MapGet("/media-files/{mediaFileId}/stream", StreamAsync).RequiresPermission(Permissions.LibraryView).AllowsSessionSignedUrl();
        app.MapGet("/media-files/{mediaFileId}/subtitles/{subtitleId}", SubtitleAsync)
            .RequiresPermission(Permissions.LibraryView).AllowsSessionSignedUrl();
        return app;
    }

    private static async Task<Results<Ok<PlaybackSessionDto>, ProblemHttpResult>> CreateSessionAsync(
        CreatePlaybackSessionRequest request, HttpContext http, PlaybackService playback, CancellationToken ct)
    {
        switch (request.Capabilities?.Player)
        {
            case "mpv":
                break;
            case "browser":
                return Problems.Result(StatusCodes.Status501NotImplemented, "playback.player_unsupported", "Can't play in the browser yet.",
                    "This server can only play to the MediaMan desktop app for now.", "Open this title in the desktop app to play it.");
            default:
                return Problems.Result(StatusCodes.Status400BadRequest, "playback.invalid", "Playback request not understood.",
                    "The request must say which player it's for: 'mpv' or 'browser'.", "Update the app, then try again.");
        }

        // The session is needed too: the URLs are signed for it. A session without a profile has no watch state to resume.
        if (http.Features.Get<CurrentUser>() is not { ProfileId: { } profileId, SessionId: { } sessionId })
        {
            return Problems.Result(StatusCodes.Status409Conflict, "profile.required", "Pick a profile first.",
                "Watch progress is kept per profile, and this session hasn't picked one.", "Choose who's watching, then press Play again.");
        }

        var (session, failure) = await playback.CreateDirectStreamAsync(request.PlayableId, profileId, sessionId, ct);
        return failure switch
        {
            null => TypedResults.Ok(session),
            PlaybackFailure.PlayableNotFound => Problems.Result(StatusCodes.Status404NotFound, "playable.not_found", "Nothing to play here.",
                "This movie or episode doesn't exist any more. A rescan may have removed it.", "Go back to the title and try again."),
            _ => Problems.Result(StatusCodes.Status409Conflict, "playback.no_file", "No file to play.",
                "None of this title's files can be found on disk.", "Reconnect the drive that holds it, then scan the library again."),
        };
    }

    private static async Task<Results<PhysicalFileHttpResult, ProblemHttpResult>> StreamAsync(
        Guid mediaFileId, PlaybackService playback, HttpResponse response, CancellationToken ct)
    {
        if (await playback.StreamAsync(mediaFileId, ct) is not { } stream)
        {
            return Problems.Result(StatusCodes.Status404NotFound, "media_file.unavailable", "This file can't be played.",
                "The file isn't on disk any more, or it doesn't hold a movie or episode.", "Go back to the title and press Play again.");
        }

        response.Headers.XContentTypeOptions = "nosniff";
        return TypedResults.PhysicalFile(stream.Path, stream.ContentType, enableRangeProcessing: true);
    }

    private static async Task<Results<PhysicalFileHttpResult, ProblemHttpResult>> SubtitleAsync(
        Guid mediaFileId, string subtitleId, PlaybackService playback, HttpResponse response, CancellationToken ct)
    {
        if (await playback.SubtitleAsync(mediaFileId, subtitleId, ct) is not { } subtitle)
        {
            return Problems.Result(StatusCodes.Status404NotFound, "subtitle.not_found", "Subtitle not found.",
                "The subtitle file isn't next to the video any more.", "Press Play again to get the current subtitles.");
        }

        response.Headers.XContentTypeOptions = "nosniff";
        return TypedResults.PhysicalFile(subtitle.Path, subtitle.ContentType);
    }
}
