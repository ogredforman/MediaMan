using MediaMan.Core.Domain;
using MediaMan.Data;
using MediaMan.Fakes;
using Microsoft.EntityFrameworkCore;

namespace MediaMan.Auth.Tests;

/// <summary>The password policy (SPEC §7.11): its rules, how it's stored, and sign-in and password changes under it.</summary>
public sealed class PasswordPolicyTests : IAsyncLifetime
{
    private const string Password = "a long password"; // lowercase and symbols only: fine by default, weak when strong

    private static readonly PasswordHasher _hasher = new(new Argon2Parameters(1024, 1, 1));
    private static readonly byte[] _key = [.. Enumerable.Range(0, InstallKey.SizeBytes).Select(i => (byte)i)];
    private static readonly PasswordPolicy _strong = new(RequirePasswords: true, StrongPasswords: true);

    private readonly FakeClock _clock = new();
    private TempDatabase _db = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _db = await TempDatabase.CreateMigratedAsync(Ct);

    public ValueTask DisposeAsync() => _db.DisposeAsync();

    [Theory]
    [InlineData("sevench", false)]
    [InlineData("eightchr", true)]
    [InlineData("ééééééé", false)] // 7 characters people see, even decomposed into more UTF-16 units below
    [InlineData("😀😀😀😀😀😀😀😀", true)]
    public void The_default_policy_needs_8_characters(string password, bool allowed)
    {
        password = password.Normalize(System.Text.NormalizationForm.FormD);
        (PasswordPolicy.Default.ProblemWith(password) is null).Should().Be(allowed);
    }

    [Theory]
    [InlineData("Abcdefgh1!", false)] // too short
    [InlineData("abcdefghijkl", false)] // one kind
    [InlineData("abcdefgh1234", false)] // two kinds
    [InlineData("Abcdefgh1234", true)] // upper, lower, digit
    [InlineData("abcdefgh123!", true)] // lower, digit, symbol
    [InlineData("ABCDEFGH IJ!", false)] // upper and symbols (a space is a symbol)
    [InlineData("ABCDEFGH IJ1", true)] // upper, symbol, digit
    [InlineData("Ünïcödé-wörd", true)] // upper, lower, symbol
    public void A_strong_password_has_12_characters_mixing_3_kinds(string password, bool allowed)
    {
        (_strong.ProblemWith(password) is null).Should().Be(allowed);
    }

    [Fact]
    public void Turning_passwords_off_keeps_the_rules_for_any_password_that_is_set()
    {
        new PasswordPolicy(false, false).ProblemWith("short").Should().NotBeNull();
        new PasswordPolicy(false, true).ProblemWith("eightchr").Should().NotBeNull();
        PasswordPolicy.Default.ProblemWith(new string('x', PasswordHasher.MaximumPasswordLength + 1)).Should().NotBeNull();
    }

    [Fact]
    public async Task The_policy_is_the_default_until_saved_and_then_what_was_saved()
    {
        await using var context = _db.CreateContext();
        (await PasswordPolicy.LoadAsync(context, Ct)).Should().Be(PasswordPolicy.Default);

        await PasswordPolicy.SaveAsync(context, new PasswordPolicy(false, true), Ct);
        await PasswordPolicy.SaveAsync(context, new PasswordPolicy(false, true), Ct);

        await using var other = _db.CreateContext();
        (await PasswordPolicy.LoadAsync(other, Ct)).Should().Be(new PasswordPolicy(false, true));
    }

    [Fact]
    public async Task Without_passwords_the_username_alone_signs_in_except_a_disabled_account()
    {
        await CreateOwnerAsync(Password);
        await SaveAsync(new PasswordPolicy(false, false));
        await AddUserAsync("viewer", hash: null, disabled: true);

        (await SignInAsync("owner", null)).Tokens!.PasswordChangeRequired.Should().BeFalse();
        (await SignInAsync(" owner ", "anything")).Tokens.Should().NotBeNull();
        (await SignInAsync("viewer", null)).Failure.Should().Be(SignInFailure.InvalidCredentials);
        (await SignInAsync("nobody", null)).Failure.Should().Be(SignInFailure.InvalidCredentials);
    }

    [Fact]
    public async Task With_passwords_an_account_without_one_signs_in_by_name_and_must_set_one()
    {
        await CreateOwnerAsync(password: null, new PasswordPolicy(false, false));

        var tokens = (await SignInAsync("owner", null)).Tokens!;

        tokens.PasswordChangeRequired.Should().BeTrue();
        var refreshed = (await RefreshAsync(tokens.RefreshToken)).Tokens!;
        refreshed.PasswordChangeRequired.Should().BeTrue("refreshing doesn't get round it");
    }

    [Fact]
    public async Task A_password_that_no_longer_meets_the_policy_signs_in_and_must_be_changed()
    {
        await CreateOwnerAsync(Password);
        (await SignInAsync("owner", Password)).Tokens!.PasswordChangeRequired.Should().BeFalse();

        await SaveAsync(_strong);

        (await SignInAsync("owner", "the wrong password")).Failure.Should().Be(SignInFailure.InvalidCredentials);
        (await SignInAsync("owner", null)).Failure.Should().Be(SignInFailure.InvalidCredentials);
        var tokens = (await SignInAsync("owner", Password)).Tokens!;
        tokens.PasswordChangeRequired.Should().BeTrue();
        await using var context = _db.CreateContext();
        (await context.Sessions.SingleAsync(s => s.Id == SessionIdOf(tokens), Ct)).PasswordChangeRequired.Should().BeTrue();
        (await context.Users.SingleAsync(Ct)).PasswordHash.Should().NotBeNull("only the session records it, not the account");
    }

    [Fact]
    public async Task Setting_a_password_clears_the_sessions_requirement_without_the_current_one()
    {
        var owner = await CreateOwnerAsync(Password);
        await SaveAsync(_strong);
        var mustChange = SessionIdOf((await SignInAsync("owner", Password)).Tokens!);

        await using var context = _db.CreateContext();
        var accounts = new AccountService(context, _hasher, _clock);
        (await accounts.SetPasswordAsync(owner, mustChange, null, "still weak password", Ct))!.Field.Should().Be("newPassword");
        (await accounts.SetPasswordAsync(owner, mustChange, null, "Correct-Horse-9", Ct)).Should().BeNull();

        (await context.Sessions.AsNoTracking().SingleAsync(s => s.Id == mustChange, Ct)).PasswordChangeRequired.Should().BeFalse();
        (await SignInAsync("owner", "Correct-Horse-9")).Tokens!.PasswordChangeRequired.Should().BeFalse();
    }

    [Fact]
    public async Task Otherwise_setting_a_password_needs_the_current_one_unless_there_is_none()
    {
        var owner = await CreateOwnerAsync(Password);
        var session = SessionIdOf((await SignInAsync("owner", Password)).Tokens!);
        var viewer = await AddUserAsync("viewer", hash: null, disabled: false);

        await using var context = _db.CreateContext();
        var accounts = new AccountService(context, _hasher, _clock);
        (await accounts.SetPasswordAsync(owner, session, null, "a new password", Ct))!.Field.Should().Be("currentPassword");
        (await accounts.SetPasswordAsync(owner, session, "wrong one", "a new password", Ct))!.Field.Should().Be("currentPassword");
        (await accounts.SetPasswordAsync(owner, session, Password, "a new password", Ct)).Should().BeNull();
        (await accounts.SetPasswordAsync(viewer, null, null, "a viewer password", Ct)).Should().BeNull();

        (await SignInAsync("owner", "a new password")).Tokens.Should().NotBeNull();
        (await SignInAsync("viewer", "a viewer password")).Tokens!.PasswordChangeRequired.Should().BeFalse();
    }

    [Fact]
    public async Task The_Superadmin_is_created_without_a_password_only_when_passwords_are_off()
    {
        await using var context = _db.CreateContext();
        var accounts = new AccountService(context, _hasher, _clock);

        await FluentActions.Invoking(() => accounts.CreateSuperadminAsync("owner", null, PasswordPolicy.Default, Ct))
            .Should().ThrowAsync<ArgumentException>();
        await FluentActions.Invoking(() => accounts.CreateSuperadminAsync("owner", "a long password", _strong, Ct))
            .Should().ThrowAsync<ArgumentException>();
        var owner = await accounts.CreateSuperadminAsync("owner", "", new PasswordPolicy(false, false), Ct);

        owner.PasswordHash.Should().BeNull();
    }

    private async Task<Guid> CreateOwnerAsync(string? password, PasswordPolicy? policy = null)
    {
        await using var context = _db.CreateContext();
        return (await new AccountService(context, _hasher, _clock)
            .CreateSuperadminAsync("owner", password, policy ?? PasswordPolicy.Default, Ct)).Id;
    }

    private async Task SaveAsync(PasswordPolicy policy)
    {
        await using var context = _db.CreateContext();
        await PasswordPolicy.SaveAsync(context, policy, Ct);
    }

    private async Task<Guid> AddUserAsync(string username, string? hash, bool disabled)
    {
        await using var context = _db.CreateContext();
        var role = await context.Roles.FirstAsync(Ct);
        var user = new User { Id = Guid.CreateVersion7(), Username = username, PasswordHash = hash, RoleId = role.Id, Disabled = disabled };
        context.Users.Add(user);
        await context.SaveChangesAsync(Ct);
        return user.Id;
    }

    private async Task<SignInResult> SignInAsync(string username, string? password)
    {
        await using var context = _db.CreateContext();
        return await new SessionService(context, _hasher, new AccessTokens(_key, _clock), _clock).SignInAsync(username, password, null, Ct);
    }

    private async Task<SignInResult> RefreshAsync(string refreshToken)
    {
        await using var context = _db.CreateContext();
        return await new SessionService(context, _hasher, new AccessTokens(_key, _clock), _clock).RefreshAsync(refreshToken, Ct);
    }

    private static Guid SessionIdOf(AuthTokens tokens) => Guid.ParseExact(tokens.RefreshToken.Split('.')[0], "N");
}
