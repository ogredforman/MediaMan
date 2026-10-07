using MediaMan.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace MediaMan.Data;

/// <summary>A change that would demote, delete or disable the Superadmin (SPEC §5, §7.11).</summary>
public sealed class SuperadminProtectedException(string message) : InvalidOperationException(message);

/// <summary>
/// Refuses, on every save, to delete the Superadmin, clear its <see cref="User.IsSuperadmin"/>, disable it or change
/// its role, whoever asks (SPEC §5, §7.11). Changes are compared with the stored row, not the tracked original values,
/// so an attached copy can't slip past. Registered on every context by <see cref="MediaManDb.Configure"/>, so no
/// endpoint can forget it. Bulk <c>ExecuteUpdate</c>/<c>ExecuteDelete</c> skip interceptors: never use them on
/// <see cref="User"/>. Ownership transfer (a later stage) will be the one sanctioned way to change the Superadmin.
/// </summary>
internal sealed class SuperadminGuard : SaveChangesInterceptor
{
    public static readonly SuperadminGuard Instance = new();

    private sealed record Stored(Guid Id, Guid RoleId);

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (ChangedUsers(eventData.Context) is { Count: > 0 } changed)
        {
            Check(changed, StoredQuery(eventData.Context!).SingleOrDefault());
        }

        return result;
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (ChangedUsers(eventData.Context) is { Count: > 0 } changed)
        {
            Check(changed, await StoredQuery(eventData.Context!).SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false));
        }

        return result;
    }

    private static List<Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<User>> ChangedUsers(DbContext? context)
    {
        if (context is null)
        {
            return [];
        }

        if (context.ChangeTracker.AutoDetectChangesEnabled)
        {
            context.ChangeTracker.DetectChanges();
        }

        return [.. context.ChangeTracker.Entries<User>().Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)];
    }

    // At most one row: the filtered unique index on IsSuperadmin guarantees it.
    private static IQueryable<Stored> StoredQuery(DbContext context) =>
        context.Set<User>().AsNoTracking().Where(u => u.IsSuperadmin).Select(u => new Stored(u.Id, u.RoleId));

    private static void Check(List<Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<User>> changed, Stored? stored)
    {
        foreach (var entry in changed)
        {
            var user = entry.Entity;
            if (entry.State == EntityState.Added)
            {
                if (user.IsSuperadmin && user.Disabled)
                {
                    throw new SuperadminProtectedException("The Superadmin can't be created disabled.");
                }

                continue;
            }

            if (stored is null || user.Id != stored.Id)
            {
                continue;
            }

            if (entry.State == EntityState.Deleted)
            {
                throw new SuperadminProtectedException("The Superadmin can't be deleted. Transfer ownership to another account first.");
            }

            if (!user.IsSuperadmin)
            {
                throw new SuperadminProtectedException("The Superadmin can't be demoted. Transfer ownership to another account instead.");
            }

            if (user.Disabled)
            {
                throw new SuperadminProtectedException("The Superadmin can't be disabled.");
            }

            if (user.RoleId != stored.RoleId)
            {
                throw new SuperadminProtectedException("The Superadmin's role can't be changed.");
            }
        }
    }
}
