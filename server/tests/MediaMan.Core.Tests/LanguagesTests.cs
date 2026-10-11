namespace MediaMan.Core.Tests;

public class LanguagesTests
{
    [Theory]
    [InlineData("en", true)]
    [InlineData("zh", true)]
    [InlineData("EN", false)]
    [InlineData("eng", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsKnown_matches_lower_case_ISO_639_1_codes_only(string? code, bool expected)
    {
        Languages.IsKnown(code).Should().Be(expected);
    }

    [Fact]
    public void MatchTags_lists_the_code_then_its_ISO_639_2_forms()
    {
        Languages.MatchTags("en").Should().Equal("en", "eng");
        Languages.MatchTags("de").Should().Equal("de", "ger", "deu");
        Languages.MatchTags("no").Should().Contain(["nob", "nno"]);
    }

    [Fact]
    public void MatchTags_refuses_an_unknown_code()
    {
        var act = () => Languages.MatchTags("xx");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Codes_are_sorted_and_two_letters()
    {
        Languages.Codes.Should().BeInAscendingOrder(StringComparer.Ordinal);
        Languages.Codes.Should().OnlyContain(code => code.Length == 2);
    }

    [Theory]
    [InlineData(new string[0], null)]
    [InlineData(new[] { "ja", "en" }, null)]
    [InlineData(new[] { "en", "fr", "de", "es", "it", "ja" }, "Pick at most 5 languages.")]
    [InlineData(new[] { "en", "xx" }, "'xx' isn't a language you can pick.")]
    [InlineData(new[] { "en", "fr", "en" }, "List each language once.")]
    [InlineData(new[] { "en", null }, "Pick languages from the list.")]
    public void ProblemWith_checks_known_codes_repeats_and_count(string?[] preferences, string? expected)
    {
        Languages.ProblemWith(preferences).Should().Be(expected);
    }
}
