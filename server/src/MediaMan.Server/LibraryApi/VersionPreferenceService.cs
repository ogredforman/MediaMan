using MediaMan.Core;
using MediaMan.Core.Domain;
using MediaMan.Data;
using MediaMan.Playback.Versions;
using Microsoft.EntityFrameworkCore;

namespace MediaMan.Server.LibraryApi;

public enum VersionPreferenceFailure
{
    TitleNotFound,
    InvalidFile,
}

/// <summary>
/// A profile's preferred version per Title (SPEC §7.4 step 2): set from the detail page's version picker, read by
/// title detail and by whatever starts playback, through <see cref="VersionPicker"/>.
/// </summary>
public sealed class VersionPreferenceService(MediaManDbContext db, IClock clock)
{
    /// <summary>Remembers <paramref name="mediaFileId"/> and its quality for the profile, replacing any earlier pick.</summary>
    public async Task<VersionPreferenceFailure?> SetAsync(Guid profileId, Guid titleId, Guid mediaFileId, CancellationToken ct)
    {
        if (!await db.Titles.AnyAsync(t => t.Id == titleId, ct))
        {
            return VersionPreferenceFailure.TitleNotFound;
        }

        var file = await (
                from link in db.FileLinks
                join f in db.MediaFiles on link.MediaFileId equals f.Id
                join p in PlayablesOf(titleId) on link.PlayableId equals p.Id
                where f.Id == mediaFileId && f.Status == MediaFileStatus.Matched
                select f)
            .AsNoTracking()
            .FirstOrDefaultAsync(ct);
        if (file is null)
        {
            return VersionPreferenceFailure.InvalidFile;
        }

        try
        {
            await SaveAsync(profileId, titleId, file, ct);
        }
        catch (DbUpdateException)
        {
            // Another request (a double click, a second device) inserted the row first: the last pick wins.
            db.ChangeTracker.Clear();
            await SaveAsync(profileId, titleId, file, ct);
        }

        return null;
    }

    private async Task SaveAsync(Guid profileId, Guid titleId, MediaFile file, CancellationToken ct)
    {
        var quality = VersionPicker.QualityOf(file);
        var preference = await db.VersionPreferences.FirstOrDefaultAsync(v => v.ProfileId == profileId && v.TitleId == titleId, ct);
        if (preference is null)
        {
            preference = new VersionPreference { ProfileId = profileId, TitleId = titleId };
            db.VersionPreferences.Add(preference);
        }

        preference.MediaFileId = file.Id;
        preference.Resolution = quality.Resolution;
        preference.HdrFormat = quality.HdrFormat;
        preference.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Forgets the profile's pick for the title; false when the title doesn't exist.</summary>
    public async Task<bool> ClearAsync(Guid profileId, Guid titleId, CancellationToken ct)
    {
        if (!await db.Titles.AnyAsync(t => t.Id == titleId, ct))
        {
            return false;
        }

        await db.VersionPreferences.Where(v => v.ProfileId == profileId && v.TitleId == titleId).ExecuteDeleteAsync(ct);
        return true;
    }

    /// <summary>The profile's preference for the Title holding <paramref name="playableId"/>, or null.</summary>
    public async Task<VersionPreference?> ForPlayableAsync(Guid? profileId, Guid playableId, CancellationToken ct)
    {
        if (profileId is not { } profile)
        {
            return null;
        }

        var titleId = await (
                from p in db.Playables
                where p.Id == playableId
                select p.TitleId ?? (
                    from e in db.Episodes
                    join s in db.Seasons on e.SeasonId equals s.Id
                    where e.Id == p.EpisodeId
                    select (Guid?)s.TitleId).FirstOrDefault())
            .FirstOrDefaultAsync(ct);
        return titleId is { } title
            ? await db.VersionPreferences.AsNoTracking().FirstOrDefaultAsync(v => v.ProfileId == profile && v.TitleId == title, ct)
            : null;
    }

    // A movie's Playable points at its Title; an episode's goes through its Season.
    private IQueryable<Playable> PlayablesOf(Guid titleId) =>
        from p in db.Playables
        where p.TitleId == titleId
            || db.Episodes.Any(e => e.Id == p.EpisodeId && db.Seasons.Any(s => s.Id == e.SeasonId && s.TitleId == titleId))
        select p;
}
