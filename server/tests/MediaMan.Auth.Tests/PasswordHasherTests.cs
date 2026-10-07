namespace MediaMan.Auth.Tests;

public class PasswordHasherTests
{
    // Cheap parameters keep the tests fast; the format and verification are the same as the default's.
    private static readonly PasswordHasher _hasher = new(new Argon2Parameters(1024, 1, 1));

    [Fact]
    public void Hash_is_a_PHC_argon2id_string_with_its_parameters()
    {
        var hash = _hasher.Hash("correct horse battery");

        hash.Should().MatchRegex(@"^\$argon2id\$v=19\$m=1024,t=1,p=1\$[A-Za-z0-9+/]{22}\$[A-Za-z0-9+/]{43}$");
        hash.Should().NotContain("correct horse");
    }

    [Fact]
    public void Verify_accepts_the_password_and_rejects_others()
    {
        var hash = _hasher.Hash("correct horse battery");

        PasswordHasher.Verify("correct horse battery", hash).Should().BeTrue();
        PasswordHasher.Verify("correct horse battery ", hash).Should().BeFalse();
        PasswordHasher.Verify("Correct horse battery", hash).Should().BeFalse();
        PasswordHasher.Verify("", hash).Should().BeFalse();
    }

    [Fact]
    public void Each_hash_has_its_own_salt()
    {
        _hasher.Hash("same password!").Should().NotBe(_hasher.Hash("same password!"));
    }

    [Fact]
    public void Verify_uses_the_parameters_stored_in_the_hash()
    {
        var oldHash = _hasher.Hash("long enough pass");

        PasswordHasher.Verify("long enough pass", oldHash).Should().BeTrue();
        new PasswordHasher(new Argon2Parameters(2048, 2, 1)).Hash("long enough pass").Should().Contain("m=2048,t=2,p=1");
    }

    [Fact]
    public void Default_uses_at_least_the_OWASP_minimum()
    {
        var p = PasswordHasher.Default.Parameters;

        p.MemoryKib.Should().BeGreaterThanOrEqualTo(19 * 1024);
        p.Iterations.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public void Overlong_passwords_are_refused()
    {
        var tooLong = new string('a', PasswordHasher.MaximumPasswordLength + 1);
        var hash = _hasher.Hash(new string('a', PasswordHasher.MaximumPasswordLength));

        var act = () => _hasher.Hash(tooLong);

        act.Should().Throw<ArgumentException>();
        PasswordHasher.Verify(tooLong, hash).Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("plaintext")]
    [InlineData("$argon2i$v=19$m=1024,t=1,p=1$c2FsdHNhbHRzYWx0$aGFzaGhhc2hoYXNoaGFzaA")]
    [InlineData("$argon2id$v=19$m=1024,t=1$c2FsdHNhbHRzYWx0$aGFzaGhhc2hoYXNoaGFzaA")]
    [InlineData("$argon2id$v=19$m=0,t=1,p=1$c2FsdHNhbHRzYWx0$aGFzaGhhc2hoYXNoaGFzaA")]
    [InlineData("$argon2id$v=19$m=1024,t=1,p=1$not base64!$aGFzaGhhc2hoYXNoaGFzaA")]
    [InlineData("$argon2id$v=19$m=1024,t=1,p=1$c2FsdHNhbHRzYWx0")]
    public void A_malformed_hash_never_matches(string hash)
    {
        PasswordHasher.Verify("anything at all", hash).Should().BeFalse();
    }
}
