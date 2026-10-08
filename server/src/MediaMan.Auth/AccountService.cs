using MediaMan.Core;
using MediaMan.Core.Domain;
using MediaMan.Data;
using Microsoft.EntityFrameworkCore;

namespace MediaMan.Auth;

/// <summary>Creates accounts and resolves what they may do (SPEC §7.11, §8 accounts).</summary>
public sealed class AccountService(MediaManDbContext db, PasswordHasher hasher, IClock clock)
{
    public const int MinimumPasswordLength = 12;
    public const int MaximumUsernameLength = 64;

    /// <summary>The preset role the Superadmin holds. Its permissions don't matter: the Superadmin has every one.</summary>
    public const string SuperadminRoleName = "Superadmin";

    /// <summary>Creates the single Superadmin (first-run setup, SPEC §7.1) with its default profile.</summary>
    /// <exception cref="ArgumentException">The username is empty or too long, or the password is too short.</exception>
    /// <exception cref="InvalidOperationException">A Superadmin already exists.</exception>
    public async Task<User> CreateSuperadminAsync(string username, string password, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(username);
        ArgumentNullException.ThrowIfNull(password);

        username = username.Trim();
        if (username.Length is 0 or > MaximumUsernameLength)
        {
            throw new ArgumentException($"The username must be 1 to {MaximumUsernameLength} characters.", nameof(username));
        }

        if (password.Length > PasswordHasher.MaximumPasswordLength)
        {
            throw new ArgumentException($"The password must be at most {PasswordHasher.MaximumPasswordLength} characters.", nameof(password));
        }

        if (password.Length < MinimumPasswordLength)
        {
            throw new ArgumentException($"The password must be at least {MinimumPasswordLength} characters.", nameof(password));
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
            PasswordHash = hasher.Hash(password),
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
