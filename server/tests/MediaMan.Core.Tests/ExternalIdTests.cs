namespace MediaMan.Core.Tests;

public class ExternalIdTests
{
    [Theory]
    [InlineData("tmdb:603", "tmdb", "603")]
    [InlineData("imdb:tt0133093", "imdb", "tt0133093")]
    [InlineData("TMDB:603", "tmdb", "603")]
    [InlineData("anilist:21:extra", "anilist", "21:extra")]
    public void Parse_splits_provider_and_value(string text, string provider, string value)
    {
        var id = ExternalId.Parse(text);

        id.Provider.Should().Be(provider);
        id.Value.Should().Be(value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("603")]
    [InlineData(":603")]
    [InlineData("tmdb:")]
    [InlineData("tmdb: 603")]
    [InlineData("tm db:603")]
    public void TryParse_rejects_malformed_text(string? text)
    {
        ExternalId.TryParse(text, out _).Should().BeFalse();
    }

    [Fact]
    public void Parse_throws_FormatException_on_malformed_text()
    {
        var act = () => ExternalId.Parse("nonsense");

        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void ToString_round_trips_through_Parse()
    {
        var id = new ExternalId(ExternalProviders.Tvdb, "81189");

        ExternalId.Parse(id.ToString()).Should().Be(id);
        id.ToString().Should().Be("tvdb:81189");
    }

    [Fact]
    public void Equality_ignores_provider_case()
    {
        new ExternalId("TMDB", "603").Should().Be(new ExternalId("tmdb", "603"));
    }

    [Fact]
    public void Default_value_formats_as_empty()
    {
        default(ExternalId).ToString().Should().BeEmpty();
    }
}
