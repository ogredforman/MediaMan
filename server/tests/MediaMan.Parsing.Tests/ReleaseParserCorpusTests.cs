using System.Text.Json;

namespace MediaMan.Parsing.Tests;

// Corpus-driven parser test (SPEC §12). New cases go into tests/corpus/release-names.json, not here.
public class ReleaseParserCorpusTests
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public static TheoryData<string, string> Cases()
    {
        using var corpus = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "corpus", "release-names.json")));
        var data = new TheoryData<string, string>();
        foreach (var item in corpus.RootElement.GetProperty("cases").EnumerateArray())
        {
            data.Add(item.GetProperty("path").GetString()!, item.GetProperty("expected").GetRawText());
        }

        return data;
    }

    [Fact]
    public void Corpus_paths_are_unique()
    {
        var paths = Cases().Select(row => row.Data.Item1).ToList();

        paths.Should().NotBeEmpty().And.OnlyHaveUniqueItems();
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Parses_corpus_case(string path, string expectedJson)
    {
        var segments = path.Split('/');

        var parsed = ReleaseParser.Parse(segments[^1], segments[..^1]);

        var actual = JsonSerializer.SerializeToElement(parsed, _json);
        using var expected = JsonDocument.Parse(expectedJson);
        foreach (var field in expected.RootElement.EnumerateObject())
        {
            actual.TryGetProperty(field.Name, out var value).Should().BeTrue("{0} is a ParsedRelease field", field.Name);
            JsonElement.DeepEquals(value, field.Value).Should().BeTrue(
                "{0} should be {1} but was {2}", field.Name, field.Value.GetRawText(), value.GetRawText());
        }
    }

    [Fact]
    public void Empty_name_parses_to_nothing()
    {
        var parsed = ReleaseParser.Parse("", []);

        parsed.Title.Should().BeNull();
        parsed.Year.Should().BeNull();
        parsed.Season.Should().BeNull();
        parsed.Episodes.Should().BeEmpty();
    }

    [Fact]
    public void Same_input_gives_equal_output()
    {
        var first = ReleaseParser.Parse("Show.S01E01-E03.mkv", ["TV", "Show"]);
        var second = ReleaseParser.Parse("Show.S01E01-E03.mkv", ["TV", "Show"]);

        second.Should().BeEquivalentTo(first);
    }
}
