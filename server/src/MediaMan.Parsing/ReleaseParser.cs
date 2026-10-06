using System.Globalization;
using System.Text.RegularExpressions;
using MediaMan.Core.Domain;

namespace MediaMan.Parsing;

/// <summary>
/// Extracts title, year, season, episodes, absolute episode, quality tags and release group from a
/// file or release name and its parent folders (SPEC §7.2 step 3). Pure: no I/O, no clock, no state;
/// the same input always gives the same output.
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

    // Regex group name → canonical value, most preferred first. Spellings match what the probe reports where they overlap.
    private static readonly (string Group, string Value)[] _sources =
    [
        ("remux", "Remux"), ("bluray", "BluRay"), ("webdl", "WEB-DL"), ("webrip", "WEBRip"),
        ("hdtv", "HDTV"), ("dvd", "DVD"), ("telesync", "Telesync"), ("cam", "CAM"),
    ];

    private static readonly (string Group, string Value)[] _codecs =
    [
        ("hevc", "HEVC"), ("h264", "H.264"), ("av1", "AV1"), ("vc1", "VC-1"), ("mpeg2", "MPEG-2"), ("xvid", "XviD"), ("divx", "DivX"),
    ];

    private static readonly (string Group, string Value)[] _hdrFormats =
    [
        ("dv", "Dolby Vision"), ("hdr10plus", "HDR10+"), ("hdr10", "HDR10"), ("hlg", "HLG"), ("hdr", "HDR"),
    ];

    private static readonly (string Group, string Value)[] _audio =
    [
        ("truehd", "TrueHD"), ("dtsx", "DTS:X"), ("dtshdma", "DTS-HD MA"), ("dtshd", "DTS-HD"), ("dts", "DTS"), ("ddplus", "DD+"), ("dd", "DD"),
        ("flac", "FLAC"), ("lpcm", "LPCM"), ("aac", "AAC"), ("opus", "Opus"), ("mp3", "MP3"),
    ];

    private static readonly (string Group, Resolution Value)[] _resolutions =
    [
        ("r2160", Resolution.P2160), ("r1440", Resolution.P1440), ("r1080", Resolution.P1080), ("r720", Resolution.P720),
    ];

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
        var absolute = file.AbsoluteEpisode;

        var innermost = parentDirs.Count > 0 ? ParseName(parentDirs[^1]) : null;
        var (folderSeason, show) = FindShowContext(parentDirs, innermost);

        if (season is null && folderSeason is not null)
        {
            season = folderSeason;
            if (absolute is not null)
            {
                // "Show - 05" inside a season folder numbers the episode within that season.
                episodes = [absolute.Value];
                absolute = null;
            }
            else
            {
                // Inside a season folder, a name without an SxxEyy marker is an episode title, not the show.
                var loose = FindLooseEpisode(file.Normalized);
                episodes = loose is null ? [] : [loose.Value.Episode];
                var prefix = loose is null ? null : ParseName(file.Normalized[..loose.Value.Index]);
                title = prefix?.Title;
                year = prefix?.Year;
            }
        }

        if (season is not null || absolute is not null)
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

        // A file inside a release folder ("Show.S01.1080p.BluRay-GRP/04.mkv") inherits the folder's tags.
        var tags = file.Tags.Or(innermost?.Tags);

        return new ParsedRelease
        {
            Title = title,
            Year = year,
            Season = season,
            Episodes = episodes,
            AbsoluteEpisode = absolute,
            Resolution = tags.Resolution,
            Source = tags.Source,
            Codec = tags.Codec,
            HdrFormat = tags.HdrFormat,
            Audio = tags.Audio,
            Group = tags.Group,
        };
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
        var (s, leadingGroup) = Normalize(name);
        var end = s.Length;
        int? season = null;
        IReadOnlyList<int> episodes = [];
        int? absolute = null;

        if (FindEpisodeMarker(s) is { } marker)
        {
            end = marker.Index;
            season = marker.Season;
            episodes = marker.Episodes;
            if (episodes.Count == 0 && AbsoluteDashRegex().Match(s, marker.Index) is { Success: true } numbered)
            {
                // Anime season numbering: "One Punch Man S2 - 01".
                episodes = [ToInt(numbered.Groups["abs"])];
            }
        }

        end = Math.Min(end, FirstTerminator(s, end));

        if (season is null && FindAbsolute(s[..end], leadingGroup is not null) is { } abs)
        {
            end = abs.Index;
            absolute = abs.Episode;
        }
        else if (season is null && leadingGroup is not null && BatchRangeRegex().Match(s[..end]) is { Success: true, Index: > 0 } batch)
        {
            // "[Group] Show (01-12)": a batch of episodes, not part of the title.
            end = batch.Index;
        }

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

        return new NameParts(CleanTitle(s[..end]), year, season, episodes, absolute, ReadTags(s, end, leadingGroup), s);
    }

    /// <summary>Strips site-name prefixes and a leading "[Group]" tag, and turns separators into spaces.</summary>
    private static (string Text, string? LeadingGroup) Normalize(string name)
    {
        var s = SitePrefixRegex().Replace(name, "");
        string? group = null;
        var lead = LeadingGroupRegex().Match(s);
        if (lead.Success)
        {
            var candidate = lead.Groups["group"].Value.Trim();
            group = candidate.Length == 0 || SiteNameRegex().IsMatch(candidate) || AnyTagRegex().IsMatch(candidate) ? null : candidate;
            s = s[lead.Length..];
        }

        return (SeparatorRegex().Replace(s, " ").Trim(), group);
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
                end = m.Index;
                break;
            }
        }

        // A "[...]" tag after the title holds metadata ("[1080p]", "[ABCD1234]"), never more title.
        var bracket = BracketTagRegex().Match(s);
        return bracket.Success && bracket.Index < end ? bracket.Index : end;
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

    /// <summary>
    /// Anime absolute numbering: "Title - 1071" anywhere, or "Title 1071" when a leading "[Group]" tag marks
    /// a fansub release. Bare numbers in other names ("300", "1917") stay part of the title.
    /// </summary>
    private static (int Index, int Episode)? FindAbsolute(string region, bool hasLeadingGroup)
    {
        var m = AbsoluteDashRegex().Match(region);
        if (!m.Success && hasLeadingGroup)
        {
            m = AbsoluteTrailingRegex().Match(region);
        }

        return m.Success && m.Index > 0 ? (m.Index, ToInt(m.Groups["abs"])) : null;
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

    /// <summary>Reads quality tags and the group from the part of the name after the title.</summary>
    private static Tags ReadTags(string s, int start, string? leadingGroup)
    {
        var audio = Pick(AudioRegex(), s, start, _audio)?.Value;
        if (AtmosRegex().IsMatch(s, start))
        {
            audio = audio is null ? "Atmos" : audio + " Atmos";
        }

        return new Tags(
            Pick(ResolutionRegex(), s, start, _resolutions)?.Value,
            Pick(SourceRegex(), s, start, _sources)?.Value,
            Pick(CodecRegex(), s, start, _codecs)?.Value,
            Pick(HdrRegex(), s, start, _hdrFormats)?.Value,
            audio,
            TrailingGroup(s, start) ?? leadingGroup);
    }

    /// <summary>Returns the most preferred value whose group matched anywhere from <paramref name="start"/> on.</summary>
    private static (string Group, T Value)? Pick<T>(Regex regex, string s, int start, (string Group, T Value)[] values)
    {
        var best = values.Length;
        for (var m = regex.Match(s, start); m.Success && best > 0; m = m.NextMatch())
        {
            for (var i = 0; i < best; i++)
            {
                if (m.Groups[values[i].Group].Success)
                {
                    best = i;
                    break;
                }
            }
        }

        return best < values.Length ? values[best] : null;
    }

    /// <summary>The scene "-GROUP" suffix, ignoring trailing "[rartv]" or "[ABCD1234]" tags.</summary>
    private static string? TrailingGroup(string s, int start)
    {
        var trimmed = TrailingBracketsRegex().Replace(s, "");
        var m = TrailingGroupRegex().Match(trimmed);
        if (!m.Success || m.Index < start)
        {
            return null;
        }

        var group = m.Groups["group"].Value;
        return char.IsAsciiDigit(group[0]) || AnyTagRegex().IsMatch(group) || NotGroupRegex().IsMatch(group) ? null : group;
    }

    private static int ToInt(Group g) => int.Parse(g.Value, CultureInfo.InvariantCulture);

    private sealed record NameParts(
        string? Title, int? Year, int? Season, IReadOnlyList<int> Episodes, int? AbsoluteEpisode, Tags Tags, string Normalized);

    private sealed record Tags(Resolution? Resolution, string? Source, string? Codec, string? HdrFormat, string? Audio, string? Group)
    {
        public Tags Or(Tags? other) => other is null
            ? this
            : new Tags(
                Resolution ?? other.Resolution,
                Source ?? other.Source,
                Codec ?? other.Codec,
                HdrFormat ?? other.HdrFormat,
                Audio ?? other.Audio,
                Group ?? other.Group);
    }

    private readonly record struct Marker(int Index, int Season, IReadOnlyList<int> Episodes);

    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    // Token patterns, matched against the normalized name ("." and "_" already turned into spaces, so
    // "DDP5.1" reads "DDP5 1" and "H.264" reads "H 264").
    private const string Start = @"(?<![a-z0-9])";
    private const string End = @"(?![a-z0-9])";
    private const string Channels = @"(?:\d\s?\d)?";

    private const string ResolutionTokens = @"(?<r2160>2160[pi]|4k|uhd|3840x2160)|(?<r1440>1440[pi]|2560x1440)|(?<r1080>1080[pi]|1920x1080)|(?<r720>720p|1280x720)";

    private const string SourceTokens =
        @"(?<remux>(?:bd|uhd)?remux)|(?<bluray>blu-?ray|bd-?rip|br-?rip|bdmv|bd25|bd50)"
        + @"|(?<webdl>web-?dl|(?<=\d{3,4}p\s)web|web(?=\s\d{3,4}p))|(?<webrip>web-?rip)"
        + @"|(?<hdtv>hdtv|pdtv|sdtv|dsr|tvrip)|(?<dvd>dvd(?:-?rip|r|5|9)?)|(?<telesync>hdts|telesync|ts-?rip)|(?<cam>hd-?cam|cam-?rip)";

    private const string CodecTokens =
        @"(?<hevc>[xh]\s?265|hevc)|(?<h264>[xh]\s?264|avc)|(?<av1>av1)|(?<vc1>vc-?1)|(?<mpeg2>mpeg-?2)|(?<xvid>xvid)|(?<divx>divx)";

    private const string HdrTokens = @"(?<dv>dv|dovi|dolby\s?vision)|(?<hdr10plus>hdr10\+|hdr10plus)|(?<hdr10>hdr10)|(?<hlg>hlg)|(?<hdr>hdr)";

    private const string AudioTokens =
        @"(?:(?<truehd>true-?hd)|(?<dtsx>dts[\s:-]?x)|(?<dtshdma>dts-?hd\s?ma|dts-?ma)|(?<dtshd>dts-?hd(?:\s?hra)?)|(?<dts>dts)"
        + @"|(?<ddplus>ddp|dd\+|e-?ac-?3)|(?<dd>dd|ac-?3|dolby\s?digital)|(?<aac>aac)|(?<flac>flac)|(?<lpcm>l?pcm)|(?<opus>opus)|(?<mp3>mp3))"
        + Channels;

    // Tokens that end a title. Audio codecs that are also words ("Opus", "DD") only count with channels attached.
    private const string TerminatorTokens =
        @"\d{3,4}[pi]|4k|uhd|blu-?ray|bd(?:rip|remux)|br-?rip|remux|web[\s-]?(?:dl|rip)|hdtv|pdtv|dvd(?:rip|scr)?|hdrip"
        + @"|[xh]\s?26[45]|hevc|avc|av1|xvid|divx|10bit|hdr(?:10)?\+?|dv|dovi|hlg|repack|proper|hdts|telesync|hd-?cam|cam-?rip"
        + @"|true-?hd|atmos|dts(?:-?(?:hd|x|ma))?|ddp|dd\+|e-?ac-?3|(?:dd|aac)\d\s?\d|flac";

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

    // "Title - 1071", "Title - 012v2"; never a year or a batch range ("- 01-12").
    [GeneratedRegex(@"\s-\s(?!(?:19|20)\d{2}(?!\d))(?<abs>\d{2,4})(?:v\d{1,2})?(?![a-z0-9])(?!-\d)", Options)]
    private static partial Regex AbsoluteDashRegex();

    // "[Group] Title 1071": a number ending the title of a fansub release.
    [GeneratedRegex(@"\s(?!(?:19|20)\d{2}(?!\d))(?<abs>\d{2,4})(?:v\d{1,2})?[\s(~-]*$", Options)]
    private static partial Regex AbsoluteTrailingRegex();

    // "(01-12)", "- 01-12", "01~24" at the end of a fansub title.
    [GeneratedRegex(@"\s(?:-\s)?\(?\d{2,4}\s?[-~]\s?\d{2,4}\)?[\s-]*$", Options)]
    private static partial Regex BatchRangeRegex();

    [GeneratedRegex(@"^specials?$", Options)]
    private static partial Regex SpecialsFolderRegex();

    // Any 1900–2099 is a year (no clock: the parser is pure), so "Blade Runner 2049" with no release
    // year parses 2049 as the year.
    [GeneratedRegex(@"(?<![a-z0-9])[(\[]?(?<year>19\d{2}|20\d{2})[)\]]?(?![a-z0-9])", Options)]
    private static partial Regex YearRegex();

    [GeneratedRegex(Start + "(?:" + TerminatorTokens + ")" + End, Options)]
    private static partial Regex TerminatorRegex();

    [GeneratedRegex(Start + "(?:" + ResolutionTokens + ")" + End, Options)]
    private static partial Regex ResolutionRegex();

    [GeneratedRegex(Start + "(?:" + SourceTokens + ")" + End, Options)]
    private static partial Regex SourceRegex();

    [GeneratedRegex(Start + "(?:" + CodecTokens + ")" + End, Options)]
    private static partial Regex CodecRegex();

    [GeneratedRegex(Start + "(?:" + HdrTokens + ")" + End, Options)]
    private static partial Regex HdrRegex();

    [GeneratedRegex(Start + AudioTokens + End, Options)]
    private static partial Regex AudioRegex();

    [GeneratedRegex(Start + "atmos" + End, Options)]
    private static partial Regex AtmosRegex();

    // A whole candidate group that is really a tag ("-720p", "-x264").
    [GeneratedRegex("^(?:" + TerminatorTokens + "|" + ResolutionTokens + "|" + SourceTokens + "|" + CodecTokens + "|" + HdrTokens + "|" + AudioTokens + ")$", Options)]
    private static partial Regex AnyTagRegex();

    // Dash suffixes that aren't groups: "WEB-DL", "S01E01-E03", "DTS-HD MA".
    [GeneratedRegex(@"^(?:e\d+|dl|rip|hd|ma|hra|x|sub|subs|dub)$", Options)]
    private static partial Regex NotGroupRegex();

    [GeneratedRegex(@"-(?<group>[a-z0-9][a-z0-9@&]{0,24})$", Options)]
    private static partial Regex TrailingGroupRegex();

    [GeneratedRegex(@"(?:\s*\[[^\]]*\])+\s*$", Options)]
    private static partial Regex TrailingBracketsRegex();

    // A leading "[Group]" tag.
    [GeneratedRegex(@"^\s*\[(?<group>[^\]]*)\]\s*", Options)]
    private static partial Regex LeadingGroupRegex();

    private const string SiteName = @"(?:www\.)?[a-z0-9-]+\.(?:com|org|net|to|io|me|cc|tv|xyz|info|ws|se|nz|in|mx|lol|ru|co|am|is|ag|sx|eu|pw|ch|tw|li|unblocked)(?![a-z0-9])";

    // "www.Site.com - ", "[ www.Site.org ] ": site names prepended by uploaders.
    [GeneratedRegex(@"^\s*(?:[\[(]\s*" + SiteName + @"\s*[\])]|www\.[a-z0-9-]+\.[a-z]{2,4}(?![a-z0-9]))\s*(?:-\s*)?", Options)]
    private static partial Regex SitePrefixRegex();

    [GeneratedRegex("^" + SiteName + "$", Options)]
    private static partial Regex SiteNameRegex();

    // A "[...]" tag after the title start, unless it holds the year ("Movie [2019]").
    [GeneratedRegex(@"(?<!^)\[(?!(?:19|20)\d{2}\])", Options)]
    private static partial Regex BracketTagRegex();

    [GeneratedRegex(@"[._\s]+", Options)]
    private static partial Regex SeparatorRegex();
}
