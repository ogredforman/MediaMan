using MediaMan.Core;
using MediaMan.Core.Domain;
using MediaMan.Data;
using MediaMan.Fakes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace MediaMan.Auth.Tests;

public class AccountServiceTests
{
    private static readonly PasswordHasher _hasher = new(new Argon2Parameters(1024, 1, 1));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Creates_the_Superadmin_with_a_hashed_password_and_the_preset_role()
    {
        await using var db = await TempDatabase.CreateMigratedAsync(Ct);
        var clock = new FakeClock();
        await using (var context = db.CreateContext())
        {
            await new AccountService(context, _hasher, clock).CreateSuperadminAsync("  owner ", "a long password", Ct);
        }

        await using var check = db.CreateContext();
        var user = await check.Users.SingleAsync(Ct);
        var role = await check.Roles.SingleAsync(r => r.Id == user.RoleId, Ct);
        user.Username.Should().Be("owner");
        user.IsSuperadmin.Should().BeTrue();
        user.Disabled.Should().BeFalse();
        user.CreatedAt.Should().Be(clock.UtcNow);
        user.PasswordHash.Should().StartWith("$argon2id$").And.NotContain("a long password");
        PasswordHasher.Verify("a long password", user.PasswordHash).Should().BeTrue();
        role.Name.Should().Be(AccountService.SuperadminRoleName);
        role.IsPreset.Should().BeTrue();
        role.Permissions.Should().BeEquivalentTo(Permissions.All);
    }

    [Fact]
    public async Task Only_one_Superadmin_can_be_created()
    {
        await using var db = await TempDatabase.CreateMigratedAsync(Ct);
        await using var context = db.CreateContext();
        var accounts = new AccountService(context, _hasher, new FakeClock());
        await accounts.CreateSuperadminAsync("owner", "a long password", Ct);

        var act = () => accounts.CreateSuperadminAsync("second", "another long one", Ct);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*already exists*");
    }

    [Fact]
    public async Task Losing_a_race_with_a_concurrent_setup_gets_the_same_clear_error()
    {
        await using var db = await TempDatabase.CreateMigratedAsync(Ct);
        var options = MediaManDb.Configure(new DbContextOptionsBuilder<MediaManDbContext>(), db.Path)
            .AddInterceptors(new ConcurrentSetup(db)).Options;
        await using var context = new MediaManDbContext(options, db.Protector);

        var act = () => new AccountService(context, _hasher, new FakeClock()).CreateSuperadminAsync("racer", "a long password", Ct);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*already exists*");
    }

    /// <summary>Another setup saves its Superadmin after this one checked for none, just before this one saves.</summary>
    private sealed class ConcurrentSetup(TempDatabase db) : SaveChangesInterceptor
    {
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            await using var other = db.CreateContext();
            var role = new Role { Id = Guid.NewGuid(), Name = "Winner" };
            other.AddRange(role, new User { Id = Guid.NewGuid(), Username = "winner", PasswordHash = "h", RoleId = role.Id, IsSuperadmin = true });
            await other.SaveChangesAsync(cancellationToken);
            return result;
        }
    }

    [Theory]
    [InlineData("owner", "elevenchars")]
    [InlineData("   ", "a long password")]
    public async Task Rejects_short_passwords_and_empty_usernames(string username, string password)
    {
        await using var db = await TempDatabase.CreateMigratedAsync(Ct);
        await using var context = db.CreateContext();

        var act = () => new AccountService(context, _hasher, new FakeClock()).CreateSuperadminAsync(username, password, Ct);

        await act.Should().ThrowAsync<ArgumentException>();
        (await context.Users.AnyAsync(Ct)).Should().BeFalse();
    }

    [Fact]
    public async Task The_created_Superadmin_is_protected()
    {
        await using var db = await TempDatabase.CreateMigratedAsync(Ct);
        await using var context = db.CreateContext();
        var user = await new AccountService(context, _hasher, new FakeClock()).CreateSuperadminAsync("owner", "a long password", Ct);

        context.Users.Remove(user);
        var act = () => context.SaveChangesAsync(Ct);

        await act.Should().ThrowAsync<SuperadminProtectedException>();
    }

    [Fact]
    public void Superadmin_has_every_permission_whatever_its_role_holds()
    {
        var user = new User { Username = "owner", PasswordHash = "h", IsSuperadmin = true };
        var empty = new Role { Name = "Empty" };

        AccountService.PermissionsOf(user, empty).Should().BeEquivalentTo(Permissions.All);
    }

    [Fact]
    public void Other_users_get_their_role_s_known_permissions_and_nothing_while_disabled()
    {
        var user = new User { Username = "guest", PasswordHash = "h" };
        var role = new Role { Name = "User", Permissions = [Permissions.LibraryView, Permissions.DownloadsRequest, "made.up"] };

        AccountService.PermissionsOf(user, role).Should().BeEquivalentTo([Permissions.LibraryView, Permissions.DownloadsRequest]);

        user.Disabled = true;
        AccountService.PermissionsOf(user, role).Should().BeEmpty();
    }
}
