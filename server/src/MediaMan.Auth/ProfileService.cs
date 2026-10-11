using MediaMan.Core;
using MediaMan.Core.Domain;
using MediaMan.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MediaMan.Auth;

public enum ProfileFailure
{
    /// <summary>A field is invalid; <see cref="ProfileResult.Errors"/> says which and why.</summary>
    Invalid,

    /// <summary>The profile doesn't exist or belongs to another account.</summary>
    NotFound,

    /// <summary>Another profile of the account has the same name, ignoring case.</summary>
    NameTaken,

    /// <summary>The account already has <see cref="ProfileService.MaximumProfiles"/> profiles.</summary>
    Limit,

    /// <summary>It's the account's only profile; an account always keeps one.</summary>
    Last,
}

/// <summary>A profile edit; a null field stays as it is. Language lists replace the old ones, most preferred first.</summary>
public sealed record ProfileChanges(
    string? Name = null,
    string? Avatar = null,
    IReadOnlyList<string>? AudioLanguages = null,
    IReadOnlyList<string>? SubtitleLanguages = null);

public sealed record ProfileResult(Profile? Profile, ProfileFailure? Failure, IReadOnlyDictionary<string, string[]>? Errors = null)
{
    public static ProfileResult Success(Profile profile) => new(profile, null);

    public static ProfileResult Failed(ProfileFailure failure) => new(null, failure);
}

/// <summary>
/// The Netflix-style profiles under an account (SPEC §6.1, §7.11 step 3, §8 accounts). Every method is scoped to one
/// account: another account's profile is <see cref="ProfileFailure.NotFound"/>.
/// </summary>
public sealed class ProfileService(MediaManDbContext db, IClock clock)
{
    public const int MaximumNameLength = 32;
    public const int MaximumProfiles = 10;

    // SQLITE_CONSTRAINT_UNIQUE; the only unique index on Profiles besides the primary key is (UserId, Name).
    private const int SqliteUniqueConstraint = 2067;

    /// <summary>The preset avatars (a colour the clients render with the profile's initial). Uploads come later.</summary>
    public static IReadOnlyList<string> Avatars { get; } = ["violet", "blue", "teal", "green", "amber", "orange", "rose", "slate"];

    /// <summary>The profile every new account starts with, named after the account.</summary>
    public static Profile DefaultFor(User user, IClock clock) => new()
    {
        Id = Ids.New(clock),
        UserId = user.Id,
        Name = user.Username.Length > MaximumNameLength ? user.Username[..MaximumNameLength].TrimEnd() : user.Username,
        Avatar = Avatars[0],
    };

    /// <summary>
    /// The account's profiles, oldest first. An account created before profiles existed gets its default one here, so
    /// every account has at least one.
    /// </summary>
    public async Task<IReadOnlyList<Profile>> ListAsync(Guid userId, CancellationToken cancellationToken)
    {
        var profiles = await OwnedBy(userId).OrderBy(p => p.Id).ToListAsync(cancellationToken);
        if (profiles.Count > 0)
        {
            return profiles;
        }

        var user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == userId, cancellationToken);
        var profile = DefaultFor(user, clock);
        db.Profiles.Add(profile);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // A concurrent list created it first.
            db.Entry(profile).State = EntityState.Detached;
            return await OwnedBy(userId).OrderBy(p => p.Id).ToListAsync(cancellationToken);
        }

        return [profile];
    }

    /// <summary>Adds a profile. Without an avatar it gets the next preset one in turn.</summary>
    public async Task<ProfileResult> CreateAsync(Guid userId, string? name, string? avatar, CancellationToken cancellationToken)
    {
        var errors = Validate(new ProfileChanges(name, avatar), nameRequired: true);
        if (errors.Count > 0)
        {
            return new ProfileResult(null, ProfileFailure.Invalid, errors);
        }

        var trimmed = name!.Trim();

        // Concurrent creates can pass this check together and overshoot the limit by a profile or two, which is harmless.
        var count = await OwnedBy(userId).CountAsync(cancellationToken);
        if (count >= MaximumProfiles)
        {
            return ProfileResult.Failed(ProfileFailure.Limit);
        }

        if (await NameTakenAsync(userId, trimmed, exceptId: null, cancellationToken))
        {
            return ProfileResult.Failed(ProfileFailure.NameTaken);
        }

        var profile = new Profile
        {
            Id = Ids.New(clock),
            UserId = userId,
            Name = trimmed,
            Avatar = avatar ?? Avatars[count % Avatars.Count],
        };
        db.Profiles.Add(profile);
        return await SaveAsync(profile, cancellationToken);
    }

    /// <summary>Renames a profile or changes its avatar or language preferences (SPEC §8 playback).</summary>
    public async Task<ProfileResult> UpdateAsync(Guid userId, Guid profileId, ProfileChanges changes, CancellationToken cancellationToken)
    {
        var errors = Validate(changes, nameRequired: false);
        if (errors.Count > 0)
        {
            return new ProfileResult(null, ProfileFailure.Invalid, errors);
        }

        var profile = await db.Profiles.SingleOrDefaultAsync(p => p.Id == profileId && p.UserId == userId, cancellationToken);
        if (profile is null)
        {
            return ProfileResult.Failed(ProfileFailure.NotFound);
        }

        if (changes.Name is { } name)
        {
            var trimmed = name.Trim();
            if (await NameTakenAsync(userId, trimmed, exceptId: profileId, cancellationToken))
            {
                return ProfileResult.Failed(ProfileFailure.NameTaken);
            }

            profile.Name = trimmed;
        }

        profile.Avatar = changes.Avatar ?? profile.Avatar;
        if (changes.AudioLanguages is { } audio)
        {
            profile.AudioLangPrefs = [.. audio];
        }

        if (changes.SubtitleLanguages is { } subtitles)
        {
            profile.SubtitleLangPrefs = [.. subtitles];
        }

        return await SaveAsync(profile, cancellationToken);
    }

    /// <summary>Deletes a profile with its watch state; sessions that had it picked go back to the picker.</summary>
    public async Task<ProfileFailure?> DeleteAsync(Guid userId, Guid profileId, CancellationToken cancellationToken)
    {
        // One statement, so two concurrent deletes can't both see another profile left and remove the last two. The
        // database cascades to WatchState and clears the sessions' pick.
        var deleted = await db.Profiles
            .Where(p => p.Id == profileId && p.UserId == userId && db.Profiles.Any(o => o.UserId == userId && o.Id != profileId))
            .ExecuteDeleteAsync(cancellationToken);
        if (deleted > 0)
        {
            return null;
        }

        return await OwnedBy(userId).AnyAsync(p => p.Id == profileId, cancellationToken) ? ProfileFailure.Last : ProfileFailure.NotFound;
    }

    /// <summary>Picks the profile a session watches as, until it picks another or signs out.</summary>
    public async Task<ProfileResult> SelectAsync(Guid userId, Guid sessionId, Guid profileId, CancellationToken cancellationToken)
    {
        var profile = await OwnedBy(userId).SingleOrDefaultAsync(p => p.Id == profileId, cancellationToken);
        if (profile is null)
        {
            return ProfileResult.Failed(ProfileFailure.NotFound);
        }

        await db.Sessions.Where(s => s.Id == sessionId && s.UserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ActiveProfileId, profileId), cancellationToken);
        return ProfileResult.Success(profile);
    }

    private IQueryable<Profile> OwnedBy(Guid userId) => db.Profiles.AsNoTracking().Where(p => p.UserId == userId);

    // Compared with NOCASE, the collation of the unique (UserId, Name) index, so the check and the index agree.
    private Task<bool> NameTakenAsync(Guid userId, string name, Guid? exceptId, CancellationToken cancellationToken) =>
        OwnedBy(userId).AnyAsync(p => EF.Functions.Collate(p.Name, "NOCASE") == name && p.Id != exceptId, cancellationToken);

    private async Task<ProfileResult> SaveAsync(Profile profile, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return ProfileResult.Success(profile);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Deleted while being renamed.
            db.ChangeTracker.Clear();
            return ProfileResult.Failed(ProfileFailure.NotFound);
        }
        catch (DbUpdateException e) when (e.InnerException is SqliteException { SqliteExtendedErrorCode: SqliteUniqueConstraint })
        {
            // Lost a race with a concurrent create or rename to the same name: the unique (UserId, Name) index refused it.
            db.ChangeTracker.Clear();
            return ProfileResult.Failed(ProfileFailure.NameTaken);
        }
    }

    private static Dictionary<string, string[]> Validate(ProfileChanges changes, bool nameRequired)
    {
        var (name, avatar, audio, subtitles) = changes;
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (name is null ? nameRequired : name.Trim().Length is 0 or > MaximumNameLength)
        {
            errors["name"] = [$"Enter a name of 1 to {MaximumNameLength} characters."];
        }

        if (avatar is not null && !Avatars.Contains(avatar, StringComparer.Ordinal))
        {
            errors["avatar"] = [$"Pick one of the preset avatars: {string.Join(", ", Avatars)}."];
        }

        if (audio is not null && Languages.ProblemWith(audio) is { } audioProblem)
        {
            errors["audioLanguages"] = [audioProblem];
        }

        if (subtitles is not null && Languages.ProblemWith(subtitles) is { } subtitleProblem)
        {
            errors["subtitleLanguages"] = [subtitleProblem];
        }

        return errors;
    }
}
