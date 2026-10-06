using System.Text.Json;
using System.Text.Json.Serialization;
using MediaMan.Core.Domain;

namespace MediaMan.Parsing.Tests;

// Corpus-driven parser test (SPEC §12). New cases go into tests/corpus/release-names.json, not here.
// A case passes when every field in its 'expected' matches; overall accuracy must stay at or above the
// corpus 'baseline'. Raise the baseline when accuracy improves; never lower it to make a change pass.
public class ReleaseParserCorpusTests
{
    private const int MinimumCases = 300;

    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly Lazy<Corpus> _corpus = new(Load);

    [Fact]
    public void Corpus_has_enough_cases()
    {
        _corpus.Value.Cases.Should().HaveCountGreaterThanOrEqualTo(MinimumCases);
    }

    [Fact]
    public void Corpus_paths_are_unique()
    {
        _corpus.Value.Cases.Select(c => c.Path).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Corpus_fields_are_parsed_release_fields()
    {
        var known = JsonSerializer.SerializeToElement(new ParsedRelease(), _json).EnumerateObject().Select(p => p.Name).ToHashSet();

        var unknown = _corpus.Value.Cases
            .SelectMany(c => c.Expected.EnumerateObject().Where(f => !known.Contains(f.Name)).Select(f => $"{c.Path}: {f.Name}"));

        unknown.Should().BeEmpty();
    }

    [Fact]
    public void Corpus_accuracy_meets_baseline()
    {
        var corpus = _corpus.Value;
        var failures = new List<string>();
        foreach (var c in corpus.Cases)
        {
            var mismatches = Mismatches(c);
            if (mismatches.Count > 0)
            {
                failures.Add($"{c.Path}: {string.Join("; ", mismatches)}");
            }
        }

        var accuracy = (double)(corpus.Cases.Count - failures.Count) / corpus.Cases.Count;
        var output = TestContext.Current.TestOutputHelper;
        output?.WriteLine($"Accuracy {accuracy:P2} ({corpus.Cases.Count - failures.Count}/{corpus.Cases.Count}), baseline {corpus.Baseline:P2}");
        failures.ForEach(f => output?.WriteLine(f));

        accuracy.Should().BeGreaterThanOrEqualTo(
            corpus.Baseline, "the corpus must not regress; failing cases:{0}{1}", Environment.NewLine, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Empty_name_parses_to_nothing()
    {
        var parsed = ReleaseParser.Parse("", []);

        parsed.Should().BeEquivalentTo(new ParsedRelease());
    }

    [Fact]
    public void Same_input_gives_equal_output()
    {
        var first = ReleaseParser.Parse("[Group] Show - 05 (1080p).mkv", ["Anime", "Show"]);
        var second = ReleaseParser.Parse("[Group] Show - 05 (1080p).mkv", ["Anime", "Show"]);

        second.Should().BeEquivalentTo(first);
    }

    private static List<string> Mismatches(CorpusCase c)
    {
        var segments = c.Path.Split('/');
        var actual = JsonSerializer.SerializeToElement(ReleaseParser.Parse(segments[^1], segments[..^1]), _json);

        var mismatches = new List<string>();
        foreach (var field in c.Expected.EnumerateObject())
        {
            if (!actual.TryGetProperty(field.Name, out var value) || !JsonElement.DeepEquals(value, field.Value))
            {
                mismatches.Add($"{field.Name} expected {field.Value.GetRawText()} was {(value.ValueKind == JsonValueKind.Undefined ? "missing" : value.GetRawText())}");
            }
        }

        return mismatches;
    }

    private static Corpus Load()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "corpus", "release-names.json")));
        var root = doc.RootElement;
        var cases = root.GetProperty("cases").EnumerateArray()
            .Select(item => new CorpusCase(item.GetProperty("path").GetString()!, item.GetProperty("expected").Clone()))
            .ToList();

        return new Corpus(root.GetProperty("baseline").GetDouble(), cases);
    }

    private sealed record Corpus(double Baseline, IReadOnlyList<CorpusCase> Cases);

    private sealed record CorpusCase(string Path, JsonElement Expected);
}
