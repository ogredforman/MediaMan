using MediaMan.Core.Domain;
using MediaMan.Data;
using MediaMan.Fakes;
using Microsoft.EntityFrameworkCore;

namespace MediaMan.Auth.Tests;

public sealed class SessionServiceTests : IAsyncLifetime
{
    private const string Password = "a long password";

    private static readonly PasswordHasher _hasher = new(new Argon2Parameters(1024, 1, 1));
    private static readonly byte[] _key = [.. Enumerable.Range(0, InstallKey.SizeBytes).Select(i => (byte)i)];

    private readonly FakeClock _clock = new();
    private TempDatabase _db = null!;
    private Guid _userId;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _db = await TempDatabase.CreateMigratedAsync(Ct);
        await using var context = _db.CreateContext();
        _userId = (await new AccountService(context, _hasher, _clock).CreateSuperadminAsync("Owner", Password, Ct)).Id;
    }

    public ValueTask DisposeAsync() => _db.DisposeAsync();

    [Fact]
    public async Task Signing_in_starts_a_session_that_stores_only_the_refresh_token_hash()
    {
        var result = await SignInAsync("owner", Password, " Living room PC ");

        var tokens = result.Tokens!;
        result.Failure.Should().BeNull();
        tokens.AccessTokenExpiresAt.Should().Be(_clock.UtcNow + AccessTokens.Lifetime);
        tokens.RefreshTokenExpiresAt.Should().Be(_clock.UtcNow + TimeSpan.FromDays(30));
        var claims = await Tokens().ValidateAsync(tokens.AccessToken);
        claims!.UserId.Should().Be(_userId);

        await using var check = _db.CreateContext();
        var session = await check.Sessions.SingleAsync(Ct);
        session.Id.Should().Be(claims.SessionId);
        session.UserId.Should().Be(_userId);
        session.DeviceName.Should().Be("Living room PC");
        session.CreatedAt.Should().Be(_clock.UtcNow);
        session.ExpiresAt.Should().Be(tokens.RefreshTokenExpiresAt);
        session.Revoked.Should().BeFalse();
        session.RefreshTokenHash.Should().NotBeEmpty().And.NotContain(tokens.RefreshToken.Split('.')[1]);
    }

    [Theory]
    [InlineData("owner", "wrong password!")]
    [InlineData("nobody", Password)]
    [InlineData("owner", "")]
    public async Task Wrong_credentials_fail_the_same_way_and_start_no_session(string username, string password)
    {
        var result = await SignInAsync(username, password);

        result.Should().Be(SignInResult.Failed(SignInFailure.InvalidCredentials));
        await using var check = _db.CreateContext();
        (await check.Sessions.CountAsync(Ct)).Should().Be(0);
    }

    [Fact]
    public async Task A_disabled_user_cannot_sign_in()
    {
        await AddUserAsync("viewer", disabled: true);

        (await SignInAsync("viewer", Password)).Should().Be(SignInResult.Failed(SignInFailure.InvalidCredentials));
    }

    [Fact]
    public async Task A_user_disabled_after_signing_in_cannot_refresh()
    {
        var id = await AddUserAsync("viewer", disabled: false);
        var tokens = (await SignInAsync("viewer", Password)).Tokens!;
        await using (var context = _db.CreateContext())
        {
            (await context.Users.SingleAsync(u => u.Id == id, Ct)).Disabled = true;
            await context.SaveChangesAsync(Ct);
        }

        (await RefreshAsync(tokens.RefreshToken)).Failure.Should().Be(SignInFailure.SessionExpired);
    }

    [Fact]
    public async Task Refreshing_rotates_the_token_and_slides_the_expiry()
    {
        var first = (await SignInAsync("owner", Password)).Tokens!;
        _clock.Advance(TimeSpan.FromDays(20));

        var second = (await RefreshAsync(first.RefreshToken)).Tokens!;

        second.RefreshToken.Should().NotBe(first.RefreshToken);
        second.RefreshTokenExpiresAt.Should().Be(_clock.UtcNow + TimeSpan.FromDays(30));
        second.AccessTokenExpiresAt.Should().Be(_clock.UtcNow + AccessTokens.Lifetime);
        (await Tokens().ValidateAsync(second.AccessToken))!.SessionId.Should().Be(SessionIdOf(first));

        await using var check = _db.CreateContext();
        var session = await check.Sessions.SingleAsync(Ct);
        session.LastSeenAt.Should().Be(_clock.UtcNow);
        session.ExpiresAt.Should().Be(second.RefreshTokenExpiresAt);
    }

    [Fact]
    public async Task Reusing_a_rotated_refresh_token_revokes_the_session()
    {
        var first = (await SignInAsync("owner", Password)).Tokens!;
        var second = (await RefreshAsync(first.RefreshToken)).Tokens!;

        (await RefreshAsync(first.RefreshToken)).Failure.Should().Be(SignInFailure.SessionExpired);

        (await RefreshAsync(second.RefreshToken)).Failure.Should().Be(SignInFailure.SessionExpired, "the whole session was revoked");
        await using var check = _db.CreateContext();
        (await check.Sessions.SingleAsync(Ct)).Revoked.Should().BeTrue();
    }

    [Fact]
    public async Task A_session_idle_for_30_days_expires()
    {
        var tokens = (await SignInAsync("owner", Password)).Tokens!;
        _clock.Advance(TimeSpan.FromDays(30));

        (await RefreshAsync(tokens.RefreshToken)).Failure.Should().Be(SignInFailure.SessionExpired);
    }

    [Fact]
    public async Task A_revoked_session_cannot_refresh()
    {
        var tokens = (await SignInAsync("owner", Password)).Tokens!;
        await using (var context = _db.CreateContext())
        {
            await Service(context).RevokeAsync(SessionIdOf(tokens), Ct);
        }

        (await RefreshAsync(tokens.RefreshToken)).Failure.Should().Be(SignInFailure.SessionExpired);
    }

    [Theory]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("0197a0c4f2b17c3e8f1a2b3c4d5e6f70.abc")]
    public async Task Unknown_refresh_tokens_fail(string refreshToken)
    {
        await SignInAsync("owner", Password);

        (await RefreshAsync(refreshToken)).Failure.Should().Be(SignInFailure.SessionExpired);
        await using var check = _db.CreateContext();
        (await check.Sessions.SingleAsync(Ct)).Revoked.Should().BeFalse();
    }

    [Fact]
    public async Task A_missing_or_long_device_name_is_normalised()
    {
        await SignInAsync("owner", Password, "   ");
        await SignInAsync("owner", Password, new string('x', 300));

        await using var check = _db.CreateContext();
        var names = await check.Sessions.OrderBy(s => s.CreatedAt).Select(s => s.DeviceName).ToListAsync(Ct);
        names.Should().Contain(SessionService.UnknownDevice);
        names.Should().Contain(new string('x', SessionService.MaximumDeviceNameLength));
    }

    private AccessTokens Tokens() => new(_key, _clock);

    private SessionService Service(MediaManDbContext context) => new(context, _hasher, Tokens(), _clock);

    private static Guid SessionIdOf(AuthTokens tokens) => Guid.ParseExact(tokens.RefreshToken.Split('.')[0], "N");

    private async Task<SignInResult> SignInAsync(string username, string password, string? deviceName = null)
    {
        await using var context = _db.CreateContext();
        return await Service(context).SignInAsync(username, password, deviceName, Ct);
    }

    private async Task<SignInResult> RefreshAsync(string refreshToken)
    {
        await using var context = _db.CreateContext();
        return await Service(context).RefreshAsync(refreshToken, Ct);
    }

    private async Task<Guid> AddUserAsync(string username, bool disabled)
    {
        await using var context = _db.CreateContext();
        var role = new Role { Id = Guid.CreateVersion7(), Name = "User " + username };
        var user = new User
        {
            Id = Guid.CreateVersion7(),
            Username = username,
            PasswordHash = _hasher.Hash(Password),
            RoleId = role.Id,
            Disabled = disabled,
            CreatedAt = _clock.UtcNow,
        };
        context.Roles.Add(role);
        context.Users.Add(user);
        await context.SaveChangesAsync(Ct);
        return user.Id;
    }
}
