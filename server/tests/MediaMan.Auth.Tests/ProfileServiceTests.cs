using MediaMan.Core.Domain;
using MediaMan.Data;
using MediaMan.Fakes;
using Microsoft.EntityFrameworkCore;

namespace MediaMan.Auth.Tests;

public sealed class ProfileServiceTests : IAsyncLifetime
{
    private readonly FakeClock _clock = new();
    private TempDatabase _db = null!;
    private User _owner = null!;
    private User _other = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _db = await TempDatabase.CreateMigratedAsync(Ct);
        await using var context = _db.CreateContext();
        var role = new Role { Id = Guid.NewGuid(), Name = "Viewer" };
        _owner = new User { Id = Guid.NewGuid(), Username = "owner", PasswordHash = "h", RoleId = role.Id };
        _other = new User { Id = Guid.NewGuid(), Username = "other", PasswordHash = "h", RoleId = role.Id };
        context.AddRange(role, _owner, _other, ProfileService.DefaultFor(_owner, _clock), ProfileService.DefaultFor(_other, _clock));
        await context.SaveChangesAsync(Ct);
    }

    public async ValueTask DisposeAsync() => await _db.DisposeAsync();

    [Fact]
    public async Task Creates_trimmed_profiles_with_the_next_preset_avatar()
    {
        var kids = await CreateAsync(_owner, "  Kids ");
        var guest = await CreateAsync(_owner, "Guest", "rose");

        kids.Profile!.Name.Should().Be("Kids");
        kids.Profile.Avatar.Should().Be(ProfileService.Avatars[1]);
        guest.Profile!.Avatar.Should().Be("rose");
        (await ListAsync(_owner)).Select(p => p.Name).Should().Equal("owner", "Kids", "Guest");
    }

    [Theory]
    [InlineData(null, null, "name")]
    [InlineData("   ", null, "name")]
    [InlineData("A name that is far too long for a profile", null, "name")]
    [InlineData("Kids", "neon", "avatar")]
    public async Task Rejects_invalid_fields_keyed_by_field(string? name, string? avatar, string field)
    {
        var result = await CreateAsync(_owner, name, avatar);

        result.Failure.Should().Be(ProfileFailure.Invalid);
        result.Errors.Should().ContainKey(field);
    }

    [Fact]
    public async Task Names_are_unique_within_an_account_ignoring_case()
    {
        await CreateAsync(_owner, "Kids");

        (await CreateAsync(_owner, "KIDS")).Failure.Should().Be(ProfileFailure.NameTaken);
        (await CreateAsync(_other, "Kids")).Profile.Should().NotBeNull("another account may use the name");
    }

    [Fact]
    public async Task An_account_has_at_most_ten_profiles()
    {
        for (var i = 1; i < ProfileService.MaximumProfiles; i++)
        {
            (await CreateAsync(_owner, $"Profile {i}")).Profile.Should().NotBeNull();
        }

        (await CreateAsync(_owner, "One too many")).Failure.Should().Be(ProfileFailure.Limit);
    }

    [Fact]
    public async Task Updates_only_the_given_fields()
    {
        var kids = (await CreateAsync(_owner, "Kids", "teal")).Profile!;
        await CreateAsync(_owner, "Guest");

        await using var context = _db.CreateContext();
        var service = new ProfileService(context, _clock);
        (await service.UpdateAsync(_owner.Id, kids.Id, " Children ", null, Ct)).Profile.Should()
            .BeEquivalentTo(new { Name = "Children", Avatar = "teal" });
        (await service.UpdateAsync(_owner.Id, kids.Id, null, "amber", Ct)).Profile.Should()
            .BeEquivalentTo(new { Name = "Children", Avatar = "amber" });
        (await service.UpdateAsync(_owner.Id, kids.Id, "guest", null, Ct)).Failure.Should().Be(ProfileFailure.NameTaken);
        (await service.UpdateAsync(_owner.Id, kids.Id, "CHILDREN", null, Ct)).Profile!.Name.Should()
            .Be("CHILDREN", "a profile may change the case of its own name");
    }

    [Fact]
    public async Task Another_accounts_profile_is_not_found()
    {
        var theirs = (await ListAsync(_other)).Single();
        await using var context = _db.CreateContext();
        var service = new ProfileService(context, _clock);

        (await service.UpdateAsync(_owner.Id, theirs.Id, "Mine", null, Ct)).Failure.Should().Be(ProfileFailure.NotFound);
        (await service.DeleteAsync(_owner.Id, theirs.Id, Ct)).Should().Be(ProfileFailure.NotFound);
        (await service.SelectAsync(_owner.Id, Guid.NewGuid(), theirs.Id, Ct)).Failure.Should().Be(ProfileFailure.NotFound);
    }

    [Fact]
    public async Task The_last_profile_cannot_be_deleted()
    {
        var only = (await ListAsync(_owner)).Single();
        await using var context = _db.CreateContext();

        (await new ProfileService(context, _clock).DeleteAsync(_owner.Id, only.Id, Ct)).Should().Be(ProfileFailure.Last);
    }

    [Fact]
    public async Task Deleting_a_profile_removes_its_watch_state_and_unpicks_it()
    {
        var kids = (await CreateAsync(_owner, "Kids")).Profile!;
        var session = new Session { Id = Guid.NewGuid(), UserId = _owner.Id, DeviceName = "TV", RefreshTokenHash = "hash" };
        await using (var context = _db.CreateContext())
        {
            var title = new Title { Id = Guid.NewGuid(), Kind = TitleKind.Movie, Name = "Film" };
            var playable = Playable.ForMovie(Guid.NewGuid(), title.Id);
            context.AddRange(title, playable, session,
                new WatchState { ProfileId = kids.Id, PlayableId = playable.Id, PositionSeconds = 60, DurationSeconds = 600 });
            await context.SaveChangesAsync(Ct);
            (await new ProfileService(context, _clock).SelectAsync(_owner.Id, session.Id, kids.Id, Ct)).Profile.Should().NotBeNull();
        }

        await using (var context = _db.CreateContext())
        {
            (await new ProfileService(context, _clock).DeleteAsync(_owner.Id, kids.Id, Ct)).Should().BeNull();
        }

        await using var check = _db.CreateContext();
        (await check.WatchStates.AnyAsync(Ct)).Should().BeFalse();
        (await check.Sessions.SingleAsync(Ct)).ActiveProfileId.Should().BeNull();
    }

    [Fact]
    public async Task Selecting_records_the_profile_on_that_session_only()
    {
        var kids = (await CreateAsync(_owner, "Kids")).Profile!;
        var tv = new Session { Id = Guid.NewGuid(), UserId = _owner.Id, DeviceName = "TV", RefreshTokenHash = "tv" };
        var phone = new Session { Id = Guid.NewGuid(), UserId = _owner.Id, DeviceName = "Phone", RefreshTokenHash = "phone" };
        await using (var context = _db.CreateContext())
        {
            context.AddRange(tv, phone);
            await context.SaveChangesAsync(Ct);
            await new ProfileService(context, _clock).SelectAsync(_owner.Id, tv.Id, kids.Id, Ct);
        }

        await using var check = _db.CreateContext();
        (await check.Sessions.SingleAsync(s => s.Id == tv.Id, Ct)).ActiveProfileId.Should().Be(kids.Id);
        (await check.Sessions.SingleAsync(s => s.Id == phone.Id, Ct)).ActiveProfileId.Should().BeNull();
    }

    [Fact]
    public async Task An_account_without_profiles_gets_its_default_when_listed()
    {
        var legacy = new User { Id = Guid.NewGuid(), Username = "an account from before profiles existed", PasswordHash = "h", RoleId = _owner.RoleId };
        await using (var context = _db.CreateContext())
        {
            context.Users.Add(legacy);
            await context.SaveChangesAsync(Ct);
        }

        var profiles = await ListAsync(legacy);

        profiles.Should().ContainSingle().Which.Name.Should().Be("an account from before profiles")
            .And.HaveLength(ProfileService.MaximumNameLength - 1, "the username is cut to the limit and trimmed");
        (await ListAsync(legacy)).Should().ContainSingle().Which.Id.Should().Be(profiles[0].Id);
    }

    private async Task<ProfileResult> CreateAsync(User user, string? name, string? avatar = null)
    {
        _clock.Advance(TimeSpan.FromMilliseconds(1)); // ids are UUIDv7, so later profiles list after earlier ones
        await using var context = _db.CreateContext();
        return await new ProfileService(context, _clock).CreateAsync(user.Id, name, avatar, Ct);
    }

    private async Task<IReadOnlyList<Profile>> ListAsync(User user)
    {
        await using var context = _db.CreateContext();
        return await new ProfileService(context, _clock).ListAsync(user.Id, Ct);
    }
}
