namespace MediaMan.Core.Domain;

public class User
{
    public Guid Id { get; set; }
    public required string Username { get; set; }
    public required string PasswordHash { get; set; }
    public Guid RoleId { get; set; }
    public bool IsSuperadmin { get; set; }
    public string? TotpSecret { get; set; }
    public bool Disabled { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>A named bundle of permissions from <see cref="Core.Permissions"/>.</summary>
public class Role
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public List<string> Permissions { get; set; } = [];
    public bool IsPreset { get; set; }
}

/// <summary>A Netflix-style viewer under an account.</summary>
public class Profile
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public required string Name { get; set; }
    public string? Avatar { get; set; }
    public string? PinHash { get; set; }
    public string? MaxRating { get; set; }
    public List<string> AudioLangPrefs { get; set; } = [];
    public List<string> SubtitleLangPrefs { get; set; } = [];
}

/// <summary>A signed-in device.</summary>
public class Session
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public required string DeviceName { get; set; }
    public required string RefreshTokenHash { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public bool Revoked { get; set; }

    /// <summary>The profile picked on this device after sign-in (SPEC §7.11 step 3); null until one is picked.</summary>
    public Guid? ActiveProfileId { get; set; }
}

public class Invite
{
    public Guid Id { get; set; }
    public required string TokenHash { get; set; }
    public Guid RoleId { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public int MaxUses { get; set; }
    public int Uses { get; set; }
}

/// <summary>A profile's progress on a Playable.</summary>
public class WatchState
{
    public Guid ProfileId { get; set; }
    public Guid PlayableId { get; set; }
    public double PositionSeconds { get; set; }
    public double DurationSeconds { get; set; }
    public bool Watched { get; set; }
    public DateTimeOffset LastWatchedAt { get; set; }
}

/// <summary>A security-relevant action. <see cref="UserId"/> is null for anonymous actions such as a failed login.</summary>
public class AuditEvent
{
    public Guid Id { get; set; }
    public Guid? UserId { get; set; }
    public required string Action { get; set; }
    public required string Target { get; set; }
    public DateTimeOffset At { get; set; }
}
