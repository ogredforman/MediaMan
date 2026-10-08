using System.Globalization;
using MediaMan.Fakes;

namespace MediaMan.Auth.Tests;

public class UrlSignerTests
{
    private const string Path = "/artwork/0199b0a8-0000-7000-8000-000000000001";

    private static readonly byte[] _key = [.. Enumerable.Range(0, InstallKey.SizeBytes).Select(i => (byte)i)];

    [Fact]
    public void A_signed_url_expires_at_the_next_utc_midnight_plus_one_day_and_verifies()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 8, 13, 30, 0, TimeSpan.Zero));
        var signer = new UrlSigner(_key, clock);

        var (exp, sig) = Split(signer.Sign(Path));

        exp.Should().Be(new DateTimeOffset(2026, 10, 10, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
        sig.Should().MatchRegex("^[A-Za-z0-9_-]{43}$");
        signer.IsValid(Path, exp, sig).Should().BeTrue();
    }

    [Fact]
    public void A_url_stays_the_same_all_utc_day_and_changes_at_midnight()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 8, 0, 0, 0, TimeSpan.Zero));
        var signer = new UrlSigner(_key, clock);

        var morning = signer.Sign(Path);
        clock.Advance(TimeSpan.FromHours(23) + TimeSpan.FromMinutes(59));
        var night = signer.Sign(Path);
        clock.Advance(TimeSpan.FromMinutes(1));
        var nextDay = signer.Sign(Path);

        night.Should().Be(morning);
        nextDay.Should().NotBe(morning);
    }

    [Fact]
    public void A_url_is_valid_until_it_expires()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 8, 23, 59, 0, TimeSpan.Zero));
        var signer = new UrlSigner(_key, clock);
        var (exp, sig) = Split(signer.Sign(Path));

        clock.Advance(TimeSpan.FromDays(1));
        signer.IsValid(Path, exp, sig).Should().BeTrue("it's signed until two midnights later");
        clock.Advance(TimeSpan.FromMinutes(1));
        signer.IsValid(Path, exp, sig).Should().BeFalse("it has expired");
    }

    [Fact]
    public void A_tampered_url_is_refused()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero));
        var signer = new UrlSigner(_key, clock);
        var (exp, sig) = Split(signer.Sign(Path));
        var flipped = (sig[0] == 'A' ? 'B' : 'A') + sig[1..];

        signer.IsValid(Path[..^1] + "2", exp, sig).Should().BeFalse("the path changed");
        signer.IsValid(Path, (long.Parse(exp, CultureInfo.InvariantCulture) + 86400).ToString(CultureInfo.InvariantCulture), sig).Should().BeFalse("the expiry was extended");
        signer.IsValid(Path, exp, flipped).Should().BeFalse("the signature changed");
        signer.IsValid(Path, exp, sig[..^1]).Should().BeFalse("the signature is truncated");
        signer.IsValid(Path, exp, null).Should().BeFalse("there's no signature");
        signer.IsValid(Path, null, sig).Should().BeFalse("there's no expiry");
        signer.IsValid(Path, "-1", sig).Should().BeFalse("the expiry isn't a number of seconds");
    }

    [Fact]
    public void A_url_signed_with_another_key_is_refused()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero));
        var other = new UrlSigner(new byte[InstallKey.SizeBytes], clock);
        var (exp, sig) = Split(other.Sign(Path));

        new UrlSigner(_key, clock).IsValid(Path, exp, sig).Should().BeFalse();
    }

    [Fact]
    public void A_path_with_a_query_cant_be_signed()
    {
        var signer = new UrlSigner(_key, new FakeClock());

        var act = () => signer.Sign(Path + "?x=1");

        act.Should().Throw<ArgumentException>();
    }

    private static (string Exp, string Sig) Split(string url)
    {
        url.Should().StartWith(Path + "?exp=");
        var query = url[(url.IndexOf('?') + 1)..].Split('&').Select(p => p.Split('=', 2)).ToDictionary(p => p[0], p => p[1]);
        query.Keys.Should().Equal("exp", "sig");
        return (query["exp"], query["sig"]);
    }
}
