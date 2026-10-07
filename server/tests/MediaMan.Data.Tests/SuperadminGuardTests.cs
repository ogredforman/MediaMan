using MediaMan.Core.Domain;
using MediaMan.Fakes;
using Microsoft.EntityFrameworkCore;

namespace MediaMan.Data.Tests;

/// <summary>Nobody can demote, delete or disable the Superadmin (SPEC §5, §7.11).</summary>
public class SuperadminGuardTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Superadmin_cannot_be_deleted()
    {
        await using var db = await SeedAsync();
        await using var context = db.CreateContext();
        context.Users.Remove(await context.Users.SingleAsync(u => u.IsSuperadmin, Ct));

        var act = () => context.SaveChangesAsync(Ct);

        await act.Should().ThrowAsync<SuperadminProtectedException>().WithMessage("*can't be deleted*");
        (await CountSuperadminsAsync(db)).Should().Be(1);
    }

    [Fact]
    public async Task Superadmin_cannot_be_demoted()
    {
        await using var db = await SeedAsync();
        await using var context = db.CreateContext();
        (await context.Users.SingleAsync(u => u.IsSuperadmin, Ct)).IsSuperadmin = false;

        var act = () => context.SaveChangesAsync(Ct);

        await act.Should().ThrowAsync<SuperadminProtectedException>().WithMessage("*can't be demoted*");
        (await CountSuperadminsAsync(db)).Should().Be(1);
    }

    [Fact]
    public async Task Superadmin_cannot_be_disabled()
    {
        await using var db = await SeedAsync();
        await using var context = db.CreateContext();
        (await context.Users.SingleAsync(u => u.IsSuperadmin, Ct)).Disabled = true;

        var act = () => context.SaveChanges();

        act.Should().Throw<SuperadminProtectedException>().WithMessage("*can't be disabled*");
    }

    [Fact]
    public async Task Superadmin_role_cannot_be_changed()
    {
        await using var db = await SeedAsync();
        await using var context = db.CreateContext();
        var other = new Role { Id = Guid.NewGuid(), Name = "User" };
        context.Roles.Add(other);
        (await context.Users.SingleAsync(u => u.IsSuperadmin, Ct)).RoleId = other.Id;

        var act = () => context.SaveChangesAsync(Ct);

        await act.Should().ThrowAsync<SuperadminProtectedException>().WithMessage("*role can't be changed*");
    }

    [Fact]
    public async Task Superadmin_cannot_be_demoted_through_a_detached_copy()
    {
        await using var db = await SeedAsync();
        Guid id;
        Guid roleId;
        await using (var read = db.CreateContext())
        {
            var admin = await read.Users.AsNoTracking().SingleAsync(u => u.IsSuperadmin, Ct);
            (id, roleId) = (admin.Id, admin.RoleId);
        }

        await using var context = db.CreateContext();
        context.Users.Update(new User { Id = id, Username = "owner", PasswordHash = "h", RoleId = roleId, IsSuperadmin = false });

        var act = () => context.SaveChangesAsync(Ct);

        await act.Should().ThrowAsync<SuperadminProtectedException>().WithMessage("*can't be demoted*");
        (await CountSuperadminsAsync(db)).Should().Be(1);
    }

    [Fact]
    public async Task Other_changes_to_the_Superadmin_and_other_users_are_allowed()
    {
        await using var db = await SeedAsync();
        await using (var context = db.CreateContext())
        {
            (await context.Users.SingleAsync(u => u.IsSuperadmin, Ct)).Username = "renamed";
            var other = await context.Users.SingleAsync(u => !u.IsSuperadmin, Ct);
            other.Disabled = true;
            await context.SaveChangesAsync(Ct);
            context.Users.Remove(other);
            await context.SaveChangesAsync(Ct);
        }

        await using var check = db.CreateContext();
        (await check.Users.Select(u => u.Username).ToListAsync(Ct)).Should().Equal("renamed");
    }

    private static async Task<TempDatabase> SeedAsync()
    {
        var db = await TempDatabase.CreateMigratedAsync(Ct);
        await using var context = db.CreateContext();
        var role = new Role { Id = Guid.NewGuid(), Name = "Superadmin" };
        context.AddRange(
            role,
            new User { Id = Guid.NewGuid(), Username = "owner", PasswordHash = "h", RoleId = role.Id, IsSuperadmin = true },
            new User { Id = Guid.NewGuid(), Username = "guest", PasswordHash = "h", RoleId = role.Id });
        await context.SaveChangesAsync(Ct);
        return db;
    }

    private static async Task<int> CountSuperadminsAsync(TempDatabase db)
    {
        await using var context = db.CreateContext();
        return await context.Users.CountAsync(u => u.IsSuperadmin, Ct);
    }
}
