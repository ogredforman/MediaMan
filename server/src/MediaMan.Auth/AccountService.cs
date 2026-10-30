using MediaMan.Core;
using MediaMan.Core.Domain;
using MediaMan.Data;
using Microsoft.EntityFrameworkCore;

namespace MediaMan.Auth;

/// <summary>Why <see cref="AccountService.SetPasswordAsync"/> refused, keyed by the request field it's about.</summary>
public sealed record PasswordChangeFailure(string Field, string Message);

/// <summary>Creates accounts, sets their passwords and resolves what they may do (SPEC §7.11, §8 accounts).</summary>
public sealed class AccountService(MediaManDbContext db, PasswordHasher hasher, IClock clock)
{
    public const int MaximumUsernameLength = 64;

    /// <summary>The preset role the Superadmin holds. Its permissions don't matter: the Superadmin has every one.</summary>
    public const string SuperadminRoleName = "Superadmin";

    /// <summary>Creates the single Superadmin under the default password policy.</summary>
    /// <inheritdoc cref="CreateSuperadminAsync(string, string?, PasswordPolicy, CancellationToken)"/>
    public Task<User> CreateSuperadminAsync(string username, string password, CancellationToken cancellationToken) =>
        CreateSuperadminAsync(username, password, PasswordPolicy.Default, cancellationToken);

    /// <summary>
    /// Creates the single Superadmin (first-run setup, SPEC §7.1) with its default profile. Without a password (null or
    /// empty), which <paramref name="policy"/> allows only when it doesn't require passwords, the account has none.
    /// </summary>
    /// <exception cref="ArgumentException">The username is empty or too long, or the password doesn't meet the policy.</exception>
    /// <exception cref="InvalidOperationException">A Superadmin already exists.</exception>
    public async Task<User> CreateSuperadminAsync(string username, string? password, PasswordPolicy policy, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(username);
        ArgumentNullException.ThrowIfNull(policy);

        username = username.Trim();
        if (username.Length is 0 or > MaximumUsernameLength)
        {
            throw new ArgumentException($"The username must be 1 to {MaximumUsernameLength} characters.", nameof(username));
        }

        if (string.IsNullOrEmpty(password))
        {
            password = null;
            if (policy.RequirePasswords)
            {
                throw new ArgumentException("A password is required.", nameof(password));
            }
        }
        else if (policy.ProblemWith(password) is { } problem)
        {
            throw new ArgumentException(problem, nameof(password));
        }

        if (await db.Users.AnyAsync(u => u.IsSuperadmin, cancellationToken))
        {
            throw new InvalidOperationException("A Superadmin already exists.");
        }

        var role = await db.Roles.SingleOrDefaultAsync(r => r.Name == SuperadminRoleName, cancellationToken);
        if (role is null)
        {
            role = new Role { Id = Ids.New(clock), Name = SuperadminRoleName, Permissions = [.. Permissions.All.Order(StringComparer.Ordinal)], IsPreset = true };
            db.Roles.Add(role);
        }

        var user = new User
        {
            Id = Ids.New(clock),
            Username = username,
            PasswordHash = password is null ? null : hasher.Hash(password),
            RoleId = role.Id,
            IsSuperadmin = true,
            CreatedAt = clock.UtcNow,
        };
        db.Users.Add(user);
        db.Profiles.Add(ProfileService.DefaultFor(user, clock));
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Lost a race with a concurrent setup: the unique index allowed only one.
            if (await AnotherSuperadminExistsAsync(user.Id, cancellationToken))
            {
                throw new InvalidOperationException("A Superadmin already exists.");
            }

            throw;
        }

        return user;
    }

    /// <summary>
    /// Sets <paramref name="userId"/>'s password to <paramref name="newPassword"/> if it meets the policy (SPEC §7.11).
    /// <paramref name="currentPassword"/> must match when the account has a password, unless
    /// <paramref name="sessionId"/> is a session that must change it (it proved the password at sign-in). That
    /// session's requirement is cleared; other sessions are left as they are.
    /// </summary>
    /// <returns>Null when the password is set; otherwise why not.</returns>
    public async Task<PasswordChangeFailure?> SetPasswordAsync(
        Guid userId, Guid? sessionId, string? currentPassword, string newPassword, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(newPassword);

        var policy = await PasswordPolicy.LoadAsync(db, cancellationToken);
        if (policy.ProblemWith(newPassword) is { } problem)
        {
            return new PasswordChangeFailure("newPassword", problem);
        }

        var user = await db.Users.SingleAsync(u => u.Id == userId, cancellationToken);
        var session = sessionId is { } sid
            ? await db.Sessions.SingleOrDefaultAsync(s => s.Id == sid && s.UserId == userId, cancellationToken)
            : null;
        if (user.PasswordHash is { } hash && session is not { PasswordChangeRequired: true }
            && (string.IsNullOrEmpty(currentPassword) || !PasswordHasher.Verify(currentPassword, hash)))
        {
            return new PasswordChangeFailure("currentPassword", string.IsNullOrEmpty(currentPassword)
                ? "Enter your current password."
                : "That isn't your current password.");
        }

        user.PasswordHash = hasher.Hash(newPassword);
        if (session is not null)
        {
            session.PasswordChangeRequired = false;
        }

        await db.SaveChangesAsync(cancellationToken);
        return null;
    }

    private Task<bool> AnotherSuperadminExistsAsync(Guid id, CancellationToken cancellationToken) =>
        db.Users.AsNoTracking().AnyAsync(u => u.IsSuperadmin && u.Id != id, cancellationToken);

    /// <summary>
    /// What <paramref name="user"/> may do: every permission for the Superadmin, nothing while disabled, otherwise the
    /// known permissions of <paramref name="role"/>.
    /// </summary>
    public static IReadOnlySet<string> PermissionsOf(User user, Role role)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(role);

        if (user.IsSuperadmin)
        {
            return Permissions.All;
        }

        return user.Disabled
            ? new HashSet<string>(StringComparer.Ordinal)
            : role.Permissions.Where(Permissions.IsKnown).ToHashSet(StringComparer.Ordinal);
    }
}
