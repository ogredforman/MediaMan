namespace MediaMan.Core.Tests;

public class InfoHashTests
{
    private const string Hex = "c12fe1c06bba254a9dc9f519b335aa7c1367a88a";

    [Fact]
    public void Parse_accepts_lowercase_hex()
    {
        InfoHash.Parse(Hex).Hex.Should().Be(Hex);
    }

    [Fact]
    public void Parse_normalises_uppercase_hex()
    {
        InfoHash.Parse(Hex.ToUpperInvariant()).Should().Be(InfoHash.Parse(Hex));
    }

    [Theory]
    [InlineData("YEX6DQDLXISUVHOJ6UM3GNNKPQJWPKEK")]
    [InlineData("yex6dqdlxisuvhoj6um3gnnkpqjwpkek")]
    public void Parse_decodes_base32_to_the_same_hash(string base32)
    {
        InfoHash.Parse(base32).Should().Be(InfoHash.Parse(Hex));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("c12fe1c06bba254a9dc9f519b335aa7c1367a88")]
    [InlineData("c12fe1c06bba254a9dc9f519b335aa7c1367a88aa")]
    [InlineData("g12fe1c06bba254a9dc9f519b335aa7c1367a88a")]
    [InlineData("YEX6DQDLXISUVHOJ6UM3GNNKPQJWPKE1")]
    public void TryParse_rejects_malformed_text(string? text)
    {
        InfoHash.TryParse(text, out _).Should().BeFalse();
    }

    [Fact]
    public void Parse_throws_FormatException_on_malformed_text()
    {
        var act = () => InfoHash.Parse("abc");

        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void ToString_is_lowercase_hex()
    {
        InfoHash.Parse(Hex.ToUpperInvariant()).ToString().Should().Be(Hex);
    }
}
