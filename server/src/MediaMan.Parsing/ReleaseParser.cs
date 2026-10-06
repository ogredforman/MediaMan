using System.Globalization;
using System.Text.RegularExpressions;
using MediaMan.Core.Domain;

namespace MediaMan.Parsing;

/// <summary>
/// Extracts title, year, season and episodes from a file or release name and its parent folders
/// (SPEC §7.2 step 3). Pure: no I/O, no clock, no state; the same input always gives the same output.
/// </summary>
public static partial class ReleaseParser
{
    private const int MaxEpisodeRange = 50;

    private static readonly HashSet<string> _videoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mkv", ".mp4", ".m4v", ".avi", ".mov", ".wmv", ".ts", ".m2ts", ".mts",
        ".webm", ".mpg", ".mpeg", ".flv", ".ogm", ".divx", ".vob",
    };

    private static readonly char[] _titleTrim = [' ', '-', '(', '[', ',', ':', '~', '+'];

    /// <summary>Parses a file or release name.</summary>
    /// <param name="fileName">The file name (with or without a video extension) or a release title.</param>
    /// <param name="parentDirs">Parent folder names, outermost first (root → immediate parent). May be empty.</param>
    public static ParsedRelease Parse(string fileName, IReadOnlyList<string> parentDirs)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(parentDirs);

        var file = ParseName(StripExtension(fileName));
        var title = file.Title;
        var year = file.Year;
        var season = file.Season;
        var episodes = file.Episodes;

        var innermost = parentDirs.Count > 0 ? ParseName(parentDirs[^1]) : null;
        var (folderSeason, show) = FindShowContext(parentDirs, innermost);

        if (season is null && folderSeason is not null)
        {
            // Inside a season folder, a name without an SxxEyy marker is an episode title, not the show.
            season = folderSeason;
            var loose = FindLooseEpisode(file.Normalized);
            episodes = loose is null ? [] : [loose.Value.Episode];
            var prefix = loose is null ? null : ParseName(file.Normalized[..loose.Value.Index]);
            title = prefix?.Title;
            year = prefix?.Year;
        }

        if (season is not null)
        {
            if (show?.Title is not null)
            {
                if (title is null)
                {
                    title = show.Title;
                    year ??= show.Year;
                }
                else if (year is null && string.Equals(title, show.Title, StringComparison.OrdinalIgnoreCase))
                {
                    year = show.Year;
                }
            }
        }
        else if (innermost?.Title is not null && innermost.Season is null && (year is null || title is null))
        {
            // Movie: a "Title (Year)" or release folder is more reliable than a bare or junk file name.
            if (innermost.Year is not null)
            {
                title = innermost.Title;
                year = innermost.Year;
            }
            else
            {
                title ??= innermost.Title;
            }
        }

        return new ParsedRelease { Title = title, Year = year, Season = season, Episodes = episodes };
    }

    private static string StripExtension(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        return _videoExtensions.Contains(extension) ? fileName[..^extension.Length] : fileName;
    }

    /// <summary>Finds the season folder (if the immediate parent is one) and the folder naming the show.</summary>
    private static (int? Season, NameParts? Show) FindShowContext(IReadOnlyList<string> parentDirs, NameParts? innermost)
    {
        if (innermost is null)
        {
            return (null, null);
        }

        if (SpecialsFolderRegex().IsMatch(innermost.Normalized))
        {
            // "Specials" names the season, not the show.
            return (0, parentDirs.Count >= 2 ? ParseName(parentDirs[^2]) : null);
        }

        var season = innermost.Episodes.Count == 0 ? innermost.Season : null;

        if (season is null || innermost.Title is not null)
        {
            return (season, innermost);
        }

        // A bare "Season 1" folder: the show is named one level up.
        return (season, parentDirs.Count >= 2 ? ParseName(parentDirs[^2]) : null);
    }

    private static NameParts ParseName(string name)
    {
        var s = Normalize(name);
        var end = s.Length;
        int? season = null;
        IReadOnlyList<int> episodes = [];

        if (FindEpisodeMarker(s) is { } marker)
        {
            end = marker.Index;
            season = marker.Season;
            episodes = marker.Episodes;
        }

        end = Math.Min(end, FirstTerminator(s, end));

        // The last year before the end wins ("Blade Runner 2049 (2017)"); one at the very start is
        // part of the title ("2001 A Space Odyssey 1968", "1917").
        Match? yearMatch = null;
        foreach (Match m in YearRegex().Matches(s))
        {
            if (m.Index > 0 && m.Index + m.Length <= end)
            {
                yearMatch = m;
            }
        }

        int? year = null;
        if (yearMatch is not null)
        {
            year = ToInt(yearMatch.Groups["year"]);
            end = yearMatch.Index;
        }

        return new NameParts(CleanTitle(s[..end]), year, season, episodes, s);
    }

    private static string Normalize(string name)
    {
        var s = LeadingGroupRegex().Replace(name, "");
        return SeparatorRegex().Replace(s, " ").Trim();
    }

    private static string? CleanTitle(string raw)
    {
        var title = raw.Trim(_titleTrim);
        return title.Length == 0 ? null : title;
    }

    private static int FirstTerminator(string s, int end)
    {
        for (var m = TerminatorRegex().Match(s); m.Success && m.Index < end; m = m.NextMatch())
        {
            if (m.Index > 0)
            {
                return m.Index;
            }
        }

        return end;
    }

    private static Marker? FindEpisodeMarker(string s)
    {
        var m = SeasonEpisodeRegex().Match(s);
        if (!m.Success)
        {
            m = CrossEpisodeRegex().Match(s);
        }

        if (m.Success)
        {
            var first = ToInt(m.Groups["first"]);
            var rest = m.Groups["next"].Captures.Select(c => int.Parse(c.Value, CultureInfo.InvariantCulture));
            return new Marker(m.Index, ToInt(m.Groups["season"]), Episodes(first, rest, m.Groups["dash"].Success));
        }

        var pack = SeasonOnlyRegex().Match(s);
        return pack.Success ? new Marker(pack.Index, ToInt(pack.Groups["season"]), []) : null;
    }

    private static (int Index, int Episode)? FindLooseEpisode(string s)
    {
        var m = EpisodeWordRegex().Match(s);
        if (!m.Success)
        {
            m = LeadingNumberRegex().Match(s);
        }

        return m.Success ? (m.Index, ToInt(m.Groups["first"])) : null;
    }

    private static int[] Episodes(int first, IEnumerable<int> rest, bool range)
    {
        var all = rest.Prepend(first).ToArray();
        var last = all[^1];
        if (range && last > first && last - first <= MaxEpisodeRange)
        {
            return Enumerable.Range(first, last - first + 1).ToArray();
        }

        return all.Distinct().ToArray();
    }

    private static int ToInt(Group g) => int.Parse(g.Value, CultureInfo.InvariantCulture);

    private sealed record NameParts(string? Title, int? Year, int? Season, IReadOnlyList<int> Episodes, string Normalized);

    private readonly record struct Marker(int Index, int Season, IReadOnlyList<int> Episodes);

    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    // S01E01, S01 E01, S01E01E02, S01E01-E03, S01E01-03
    [GeneratedRegex(@"(?<![a-z0-9])s(?<season>\d{1,2})\s?e(?<first>\d{1,3})(?!\d)(?:\s?(?<dash>-)?\s?e(?<next>\d{1,3})(?!\d))*(?:(?<dash>-)(?<next>\d{1,3}))?(?![a-z0-9])", Options)]
    private static partial Regex SeasonEpisodeRegex();

    // 1x01, 1x01-02, 1x01-1x02
    [GeneratedRegex(@"(?<![a-z0-9])(?<season>\d{1,2})x(?<first>\d{2,3})(?:(?<dash>-)(?:\d{1,2}x)?(?<next>\d{2,3}))*(?![a-z0-9])", Options)]
    private static partial Regex CrossEpisodeRegex();

    // S01, Season 1, Series 1 (season packs and season folders)
    [GeneratedRegex(@"(?<![a-z0-9])(?:s(?<season>\d{1,2})|(?:season|series)\s?(?<season>\d{1,3}))(?![a-z0-9])", Options)]
    private static partial Regex SeasonOnlyRegex();

    // E01, Ep 1, Episode 1: only trusted inside a season folder.
    [GeneratedRegex(@"(?<![a-z0-9])(?:e|ep|episode)\s?(?<first>\d{1,3})(?![a-z0-9])", Options)]
    private static partial Regex EpisodeWordRegex();

    // "01 - Pilot": only trusted inside a season folder.
    [GeneratedRegex(@"^(?<first>\d{1,3})(?![a-z0-9])", Options)]
    private static partial Regex LeadingNumberRegex();

    [GeneratedRegex(@"^specials?$", Options)]
    private static partial Regex SpecialsFolderRegex();

    // Any 1900–2099 is a year (no clock: the parser is pure), so "Blade Runner 2049" with no release
    // year parses 2049 as the year. Revisit with the PLAN 1.4 corpus.
    [GeneratedRegex(@"(?<![a-z0-9])[(\[]?(?<year>19\d{2}|20\d{2})[)\]]?(?![a-z0-9])", Options)]
    private static partial Regex YearRegex();

    // Quality tokens that end a title. Extracting their values is PLAN 1.4.
    [GeneratedRegex(@"(?<![a-z0-9])(?:\d{3,4}[pi]|4k|uhd|blu-?ray|bd(?:rip|remux)|br-?rip|remux|web[\s-]?(?:dl|rip)|hdtv|pdtv|dvd(?:rip|scr)?|hdrip|[xh]\s?26[45]|hevc|avc|xvid|divx|10bit|hdr(?:10)?|repack|proper)(?![a-z0-9])", Options)]
    private static partial Regex TerminatorRegex();

    // A leading "[Group]" tag.
    [GeneratedRegex(@"^\s*\[[^\]]*\]\s*", Options)]
    private static partial Regex LeadingGroupRegex();

    [GeneratedRegex(@"[._\s]+", Options)]
    private static partial Regex SeparatorRegex();
}
