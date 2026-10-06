using System.Text.RegularExpressions;
using MediaMan.Core.Domain;
using MediaMan.Core.Modules;

namespace MediaMan.Fakes;

/// <summary>
/// Maps file-name patterns (<c>*</c> and <c>?</c> wildcards, case-insensitive) to probe results, so tests can use
/// empty placeholder files (SPEC §10.4). The first matching rule wins; with no match it returns <see cref="Default"/>.
/// Like ffprobe, it fails for a file that doesn't exist.
/// </summary>
public sealed class FakeMediaProbe : IMediaProbe
{
    public static readonly ProbeResult Hd1080 = new()
    {
        Container = "matroska,webm",
        Duration = TimeSpan.FromMinutes(45),
        Streams =
        [
            new() { Index = 0, Type = MediaStreamType.Video, Codec = "h264", Width = 1920, Height = 1080, IsDefault = true },
            new() { Index = 1, Type = MediaStreamType.Audio, Codec = "aac", Language = "eng", Channels = 2, IsDefault = true },
            new() { Index = 2, Type = MediaStreamType.Subtitle, Codec = "subrip", Language = "eng" },
        ],
    };

    public static readonly ProbeResult Uhd2160Hdr10 = new()
    {
        Container = "matroska,webm",
        Duration = TimeSpan.FromMinutes(120),
        HdrFormat = "HDR10",
        Streams =
        [
            new() { Index = 0, Type = MediaStreamType.Video, Codec = "hevc", Width = 3840, Height = 2160, IsDefault = true },
            new() { Index = 1, Type = MediaStreamType.Audio, Codec = "eac3", Language = "eng", Channels = 6, IsDefault = true },
        ],
    };

    public static readonly ProbeResult DolbyVision = Uhd2160Hdr10 with { HdrFormat = "Dolby Vision" };

    private readonly Lock _gate = new();
    private readonly List<(Regex Pattern, ProbeResult? Result)> _rules = [];
    private readonly List<string> _probed = [];

    public ProbeResult Default { get; set; } = Hd1080;

    public IReadOnlyList<string> ProbedPaths
    {
        get
        {
            lock (_gate)
            {
                return [.. _probed];
            }
        }
    }

    /// <summary>Files whose name matches <paramref name="fileNamePattern"/> probe as <paramref name="result"/>.</summary>
    public FakeMediaProbe Map(string fileNamePattern, ProbeResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return AddRule(fileNamePattern, result);
    }

    /// <summary>Files whose name matches <paramref name="fileNamePattern"/> are unreadable: probing throws <see cref="InvalidDataException"/>.</summary>
    public FakeMediaProbe Fail(string fileNamePattern) => AddRule(fileNamePattern, null);

    public Task<ProbeResult> ProbeAsync(string path, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var fileName = Path.GetFileName(path);
        lock (_gate)
        {
            _probed.Add(path);
            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"Fake probe: '{path}' does not exist.", path);
            }

            foreach (var (pattern, result) in _rules)
            {
                if (pattern.IsMatch(fileName))
                {
                    return result is null
                        ? throw new InvalidDataException($"Fake probe: '{fileName}' is not a readable media file.")
                        : Task.FromResult(result);
                }
            }

            return Task.FromResult(Default);
        }
    }

    private FakeMediaProbe AddRule(string fileNamePattern, ProbeResult? result)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileNamePattern);
        var regex = "^" + Regex.Escape(fileNamePattern).Replace(@"\*", ".*", StringComparison.Ordinal)
            .Replace(@"\?", ".", StringComparison.Ordinal) + "$";
        lock (_gate)
        {
            _rules.Add((new Regex(regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), result));
        }

        return this;
    }
}
