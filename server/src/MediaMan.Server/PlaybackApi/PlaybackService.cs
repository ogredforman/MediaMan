using MediaMan.Auth;
using MediaMan.Core.Domain;
using MediaMan.Data;
using MediaMan.Playback.Progress;
using MediaMan.Playback.Streaming;
using MediaMan.Playback.Subtitles;
using MediaMan.Playback.Versions;
using MediaMan.Server.LibraryApi;
using Microsoft.EntityFrameworkCore;

namespace MediaMan.Server.PlaybackApi;

public enum PlaybackFailure
{
    PlayableNotFound,
    NoFile,
}

/// <summary>
/// Playback sessions (SPEC §7.4): picks the version, signs its stream and subtitle URLs for the requesting session,
/// and finds the resume position. Files are only read, never changed (SPEC §5).
/// </summary>
public sealed class PlaybackService(MediaManDbContext db, VersionPreferenceService preferences, UrlSigner signer)
{
    public static string StreamPathOf(Guid mediaFileId) => $"/media-files/{mediaFileId}/stream";

    public static string SubtitlePathOf(Guid mediaFileId, string subtitleId) => $"/media-files/{mediaFileId}/subtitles/{subtitleId}";

    /// <summary>
    /// The MPV session for <paramref name="playableId"/>: the profile's preferred version among the files on disk,
    /// else the highest quality, with URLs signed for <paramref name="sessionId"/>.
    /// </summary>
    public async Task<(PlaybackSessionDto? Session, PlaybackFailure? Failure)> CreateDirectStreamAsync(
        Guid playableId, Guid profileId, Guid sessionId, CancellationToken ct)
    {
        var playable = await db.Playables.AsNoTracking().SingleOrDefaultAsync(p => p.Id == playableId, ct);
        if (playable is null)
        {
            return (null, PlaybackFailure.PlayableNotFound);
        }

        if (await PickFileAsync(playableId, profileId, ct) is not { } file)
        {
            return (null, PlaybackFailure.NoFile);
        }

        var watch = await db.WatchStates.AsNoTracking()
            .SingleOrDefaultAsync(w => w.ProfileId == profileId && w.PlayableId == playableId, ct);
        var duration = file.Probe?.Duration?.TotalSeconds;
        var subtitles = SidecarSubtitles.Find(file.Path)
            .Select(s => new SubtitleTrackDto(
                signer.SignForSession(SubtitlePathOf(file.Id, s.Id), sessionId), s.Format, s.FileName, s.Language, s.Forced))
            .ToList();

        return (new PlaybackSessionDto(
            playableId,
            file.Id,
            "directStream",
            signer.SignForSession(StreamPathOf(file.Id), sessionId),
            await DisplayNameAsync(playable, ct),
            watch is null ? null : WatchRules.ResumeAt(watch.PositionSeconds, watch.DurationSeconds > 0 ? watch.DurationSeconds : duration ?? 0),
            duration,
            subtitles), null);
    }

    /// <summary>The version to play among the Playable's files present on disk: the profile's preference, else the highest quality.</summary>
    public async Task<MediaFile?> PickFileAsync(Guid playableId, Guid? profileId, CancellationToken ct)
    {
        var files = await db.FileLinks.AsNoTracking()
            .Where(l => l.PlayableId == playableId)
            .Join(db.MediaFiles.AsNoTracking(), l => l.MediaFileId, f => f.Id, (_, f) => f)
            .ToListAsync(ct);
        var preference = await preferences.ForPlayableAsync(profileId, playableId, ct);
        return VersionPicker.Pick(files.Where(f => File.Exists(f.Path)), preference);
    }

    /// <summary>The file to stream and its content type: it must hold a Playable, not be missing, and be on disk.</summary>
    public async Task<(string Path, string ContentType)?> StreamAsync(Guid mediaFileId, CancellationToken ct)
    {
        if (await PlayableFileAsync(mediaFileId, ct) is not { } file)
        {
            return null;
        }

        return (file.Path, VideoContentTypes.Of(file.Path));
    }

    /// <summary>A sidecar subtitle of a streamable file, found by id among the files next to it now.</summary>
    public async Task<SidecarSubtitle?> SubtitleAsync(Guid mediaFileId, string subtitleId, CancellationToken ct) =>
        await PlayableFileAsync(mediaFileId, ct) is { } file
            ? SidecarSubtitles.Find(file.Path).FirstOrDefault(s => s.Id == subtitleId && File.Exists(s.Path))
            : null;

    /// <summary>The player's title: "Name (Year)" for a movie, "Show S01E02 · Episode name" for an episode.</summary>
    public async Task<string> DisplayNameAsync(Playable playable, CancellationToken ct)
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

    private async Task<MediaFile?> PlayableFileAsync(Guid mediaFileId, CancellationToken ct)
    {
        var file = await db.MediaFiles.AsNoTracking()
            .Where(f => f.Id == mediaFileId && f.Status != MediaFileStatus.Missing && db.FileLinks.Any(l => l.MediaFileId == f.Id))
            .SingleOrDefaultAsync(ct);
        return file is not null && File.Exists(file.Path) ? file : null;
    }
}
