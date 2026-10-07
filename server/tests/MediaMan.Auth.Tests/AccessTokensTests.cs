using System.Text;
using MediaMan.Fakes;
using Microsoft.IdentityModel.JsonWebTokens;

namespace MediaMan.Auth.Tests;

public class AccessTokensTests
{
    private static readonly byte[] _key = [.. Enumerable.Range(0, InstallKey.SizeBytes).Select(i => (byte)i)];

    [Fact]
    public async Task A_token_carries_the_user_and_session_and_expires_after_15_minutes()
    {
        var clock = new FakeClock();
        var tokens = new AccessTokens(_key, clock);
        var userId = Guid.CreateVersion7();
        var sessionId = Guid.CreateVersion7();

        var (token, expiresAt) = tokens.Issue(userId, sessionId);

        expiresAt.Should().Be(clock.UtcNow + TimeSpan.FromMinutes(15));
        var jwt = new JsonWebToken(token);
        jwt.Alg.Should().Be("HS256");
        jwt.Subject.Should().Be(userId.ToString());
        jwt.GetPayloadValue<string>("sid").Should().Be(sessionId.ToString());
        (await tokens.ValidateAsync(token)).Should().Be(new AccessTokenClaims(userId, sessionId));
    }

    [Fact]
    public async Task An_expired_token_is_refused()
    {
        var clock = new FakeClock();
        var tokens = new AccessTokens(_key, clock);
        var (token, _) = tokens.Issue(Guid.CreateVersion7(), Guid.CreateVersion7());

        clock.Advance(TimeSpan.FromMinutes(14));
        (await tokens.ValidateAsync(token)).Should().NotBeNull();
        clock.Advance(TimeSpan.FromMinutes(1));
        (await tokens.ValidateAsync(token)).Should().BeNull();
    }

    [Fact]
    public async Task A_token_signed_by_another_install_is_refused()
    {
        var clock = new FakeClock();
        var otherKey = InstallKey.FromBytes(_key).Derive(InstallKey.AccessTokens);
        var (token, _) = new AccessTokens(otherKey, clock).Issue(Guid.CreateVersion7(), Guid.CreateVersion7());

        (await new AccessTokens(_key, clock).ValidateAsync(token)).Should().BeNull();
    }

    [Fact]
    public async Task A_tampered_token_is_refused()
    {
        var tokens = new AccessTokens(_key, new FakeClock());
        var (token, _) = tokens.Issue(Guid.CreateVersion7(), Guid.CreateVersion7());
        var parts = token.Split('.');
        var payload = Encoding.UTF8.GetString(Convert.FromBase64String(Pad(parts[1])))
            .Replace(new JsonWebToken(token).Subject, Guid.CreateVersion7().ToString(), StringComparison.Ordinal);
        var forged = $"{parts[0]}.{Convert.ToBase64String(Encoding.UTF8.GetBytes(payload)).TrimEnd('=').Replace('+', '-').Replace('/', '_')}.{parts[2]}";

        (await tokens.ValidateAsync(forged)).Should().BeNull();
        (await tokens.ValidateAsync("not a token")).Should().BeNull();
    }

    [Fact]
    public void A_short_signing_key_is_rejected()
    {
        var act = () => new AccessTokens(new byte[16], new FakeClock());

        act.Should().Throw<ArgumentException>();
    }

    private static string Pad(string base64Url)
    {
        var s = base64Url.Replace('-', '+').Replace('_', '/');
        return s.PadRight(s.Length + ((4 - (s.Length % 4)) % 4), '=');
    }
}
