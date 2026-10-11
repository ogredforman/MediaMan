namespace MediaMan.Core;

/// <summary>
/// The languages a profile can prefer for audio and subtitles (SPEC §8 playback), keyed by ISO 639-1 code, the
/// contract's <c>LanguageCode</c>. Containers usually tag tracks with ISO 639-2 codes (bibliographic <c>ger</c> or
/// terminologic <c>deu</c>) while sidecar names use 639-1 (<c>Alien.de.srt</c>), so each code carries every form.
/// </summary>
public static class Languages
{
    public const int MaximumPreferences = 5;

    private static readonly Dictionary<string, string[]> _tags = new(StringComparer.Ordinal)
    {
        ["ar"] = ["ara"],
        ["bg"] = ["bul"],
        ["bn"] = ["ben"],
        ["ca"] = ["cat"],
        ["cs"] = ["cze", "ces"],
        ["da"] = ["dan"],
        ["de"] = ["ger", "deu"],
        ["el"] = ["gre", "ell"],
        ["en"] = ["eng"],
        ["es"] = ["spa"],
        ["et"] = ["est"],
        ["eu"] = ["baq", "eus"],
        ["fa"] = ["per", "fas"],
        ["fi"] = ["fin"],
        ["fr"] = ["fre", "fra"],
        ["gl"] = ["glg"],
        ["he"] = ["heb"],
        ["hi"] = ["hin"],
        ["hr"] = ["hrv"],
        ["hu"] = ["hun"],
        ["id"] = ["ind"],
        ["is"] = ["ice", "isl"],
        ["it"] = ["ita"],
        ["ja"] = ["jpn"],
        ["ko"] = ["kor"],
        ["lt"] = ["lit"],
        ["lv"] = ["lav"],
        ["ms"] = ["may", "msa"],
        ["nl"] = ["dut", "nld"],
        ["no"] = ["nor", "nb", "nob", "nn", "nno"],
        ["pl"] = ["pol"],
        ["pt"] = ["por"],
        ["ro"] = ["rum", "ron"],
        ["ru"] = ["rus"],
        ["sk"] = ["slo", "slk"],
        ["sl"] = ["slv"],
        ["sr"] = ["srp"],
        ["sv"] = ["swe"],
        ["ta"] = ["tam"],
        ["te"] = ["tel"],
        ["th"] = ["tha"],
        ["tl"] = ["tgl", "fil"],
        ["tr"] = ["tur"],
        ["uk"] = ["ukr"],
        ["vi"] = ["vie"],
        ["zh"] = ["chi", "zho"],
    };

    /// <summary>Every code a profile can prefer, in alphabetical order.</summary>
    public static IReadOnlyList<string> Codes { get; } = [.. _tags.Keys.Order(StringComparer.Ordinal)];

    public static bool IsKnown(string? code) => code is not null && _tags.ContainsKey(code);

    /// <summary>The tags media may use for <paramref name="code"/>, the code itself first.</summary>
    public static IReadOnlyList<string> MatchTags(string code) =>
        _tags.TryGetValue(code, out var tags) ? [code, .. tags] : throw new ArgumentException($"'{code}' isn't a known language.", nameof(code));

    /// <summary>Why a preference list is invalid, or null when it's fine: known codes, no repeats, at most five.</summary>
    public static string? ProblemWith(IReadOnlyList<string?> preferences)
    {
        if (preferences.Count > MaximumPreferences)
        {
            return $"Pick at most {MaximumPreferences} languages.";
        }

        // JSON bodies can hold nulls whatever the type says, so an unknown entry may be one.
        if (preferences.Any(code => !IsKnown(code)))
        {
            return preferences.First(code => !IsKnown(code)) is { } unknown
                ? $"'{unknown}' isn't a language you can pick."
                : "Pick languages from the list.";
        }

        return preferences.Distinct(StringComparer.Ordinal).Count() != preferences.Count ? "List each language once." : null;
    }
}
