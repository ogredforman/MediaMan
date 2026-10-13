using MediaMan.Auth;
using MediaMan.Core;
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
        // A profile deleted since the session picked it plays without preferences rather than failing.
        var profile = await db.Profiles.AsNoTracking().SingleOrDefaultAsync(p => p.Id == profileId, ct);
        var neighbours = playable.EpisodeId is { } episodeId ? await EpisodeNeighboursAsync(episodeId, ct) : default;

        return (new PlaybackSessionDto(
            playableId,
            file.Id,
            "directStream",
            signer.SignForSession(StreamPathOf(file.Id), sessionId),
            await DisplayNameAsync(playable, ct),
            watch is null ? null : WatchRules.ResumeAt(watch.PositionSeconds, watch.DurationSeconds > 0 ? watch.DurationSeconds : duration ?? 0),
            duration,
            subtitles,
            MatchTagsOf(profile?.AudioLangPrefs ?? []),
            MatchTagsOf(profile?.SubtitleLangPrefs ?? []),
            neighbours.Previous,
            neighbours.Next), null);
    }

    /// <summary>
    /// The episodes before and after <paramref name="episodeId"/> by <see cref="WatchRules.PreviousEpisode"/> and
    /// <see cref="WatchRules.NextEpisode"/>, among the show's episodes with a file on disk.
    /// </summary>
    public async Task<(NextEpisodeDto? Previous, NextEpisodeDto? Next)> EpisodeNeighboursAsync(Guid episodeId, CancellationToken ct)
    {
        var (current, candidates) = await EpisodeCandidatesAsync(episodeId, ct);
        return (await NeighbourAsync(WatchRules.PreviousEpisode(current, candidates.Keys), candidates, ct),
            await NeighbourAsync(WatchRules.NextEpisode(current, candidates.Keys), candidates, ct));
    }

    /// <summary>The episode after <paramref name="episodeId"/>, as <see cref="EpisodeNeighboursAsync"/> finds it.</summary>
    public async Task<NextEpisodeDto?> NextEpisodeAsync(Guid episodeId, CancellationToken ct)
    {
        var (current, candidates) = await EpisodeCandidatesAsync(episodeId, ct);
        return await NeighbourAsync(WatchRules.NextEpisode(current, candidates.Keys), candidates, ct);
    }

    /// <summary>The episode's slot, and the show's episodes with a file on disk by slot.</summary>
    private async Task<(EpisodeSlot Current, Dictionary<EpisodeSlot, Guid> Candidates)> EpisodeCandidatesAsync(Guid episodeId, CancellationToken ct)
    {
        var current = await db.Episodes.AsNoTracking()
            .Where(e => e.Id == episodeId)
            .Join(db.Seasons.AsNoTracking(), e => e.SeasonId, s => s.Id, (e, s) => new { s.TitleId, Slot = new EpisodeSlot(s.Number, e.Number) })
            .SingleAsync(ct);

        // The show's episodes that hold a file not marked missing; the disk check below drops ones that are gone.
        var linked = await db.Seasons.AsNoTracking()
            .Where(s => s.TitleId == current.TitleId)
            .Join(db.Episodes.AsNoTracking(), s => s.Id, e => e.SeasonId, (s, e) => new { s.Number, Episode = e })
            .Join(db.Playables.AsNoTracking(), x => x.Episode.Id, p => p.EpisodeId, (x, p) => new { x.Number, EpisodeNumber = x.Episode.Number, PlayableId = p.Id })
            .Join(db.FileLinks.AsNoTracking(), x => x.PlayableId, l => l.PlayableId, (x, l) => new { x.Number, x.EpisodeNumber, x.PlayableId, l.MediaFileId })
            .Join(db.MediaFiles.AsNoTracking().Where(f => f.Status != MediaFileStatus.Missing), x => x.MediaFileId, f => f.Id,
                (x, f) => new { x.Number, x.EpisodeNumber, x.PlayableId, f.Path })
            .ToListAsync(ct);
        var candidates = linked
            .Where(x => File.Exists(x.Path))
            .GroupBy(x => new EpisodeSlot(x.Number, x.EpisodeNumber))
            .ToDictionary(g => g.Key, g => g.First().PlayableId);
        return (current.Slot, candidates);
    }

    private async Task<NextEpisodeDto?> NeighbourAsync(EpisodeSlot? slot, Dictionary<EpisodeSlot, Guid> candidates, CancellationToken ct)
    {
        if (slot is not { } found)
        {
            return null;
        }

        var playable = await db.Playables.AsNoTracking().SingleAsync(p => p.Id == candidates[found], ct);
        return new NextEpisodeDto(playable.Id, found.SeasonNumber, found.EpisodeNumber, await DisplayNameAsync(playable, ct));
    }

    /// <summary>The tags media may use for each preferred language, in preference order; unknown codes are skipped.</summary>
    public static IReadOnlyList<string> MatchTagsOf(IEnumerable<string> preferences) =>
        [.. preferences.Where(Languages.IsKnown).SelectMany(Languages.MatchTags).Distinct(StringComparer.Ordinal)];

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
