using System.Security.Cryptography;
using MediaMan.Fakes;

namespace MediaMan.Data.Tests;

public class AesGcmSecretProtectorTests
{
    private readonly AesGcmSecretProtector _protector = new(TempDatabase.TestKey);

    [Fact]
    public void Protect_round_trips_and_hides_the_plaintext()
    {
        var stored = _protector.Protect("JBSWY3DPEHPK3PXP");

        stored.Should().StartWith("v1:").And.NotContain("JBSWY3DPEHPK3PXP");
        _protector.Unprotect(stored).Should().Be("JBSWY3DPEHPK3PXP");
    }

    [Fact]
    public void Protect_uses_a_fresh_nonce_each_time()
    {
        _protector.Protect("same").Should().NotBe(_protector.Protect("same"));
    }

    [Fact]
    public void Unprotect_rejects_a_tampered_value()
    {
        var stored = _protector.Protect("secret");
        var bytes = Convert.FromBase64String(stored[3..]);
        bytes[^1] ^= 0xFF;

        var act = () => _protector.Unprotect("v1:" + Convert.ToBase64String(bytes));

        act.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void Unprotect_rejects_a_value_from_another_key()
    {
        var other = new AesGcmSecretProtector(new byte[AesGcmSecretProtector.KeySizeBytes]);

        var act = () => _protector.Unprotect(other.Protect("secret"));

        act.Should().Throw<CryptographicException>();
    }

    [Theory]
    [InlineData("plain")]
    [InlineData("v1:not base64!")]
    [InlineData("v1:AAAA")]
    public void Unprotect_rejects_malformed_values(string stored)
    {
        var act = () => _protector.Unprotect(stored);

        act.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void Constructor_requires_a_256_bit_key()
    {
        var act = () => new AesGcmSecretProtector(new byte[16]);

        act.Should().Throw<ArgumentException>();
    }
}
