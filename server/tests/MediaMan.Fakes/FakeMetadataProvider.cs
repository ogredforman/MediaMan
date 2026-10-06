using System.Globalization;
using System.Net;
using System.Text;
using MediaMan.Core;
using MediaMan.Core.Domain;
using MediaMan.Core.Modules;

namespace MediaMan.Fakes;

/// <summary>An error the fake provider throws on a call, as a real one would (SPEC §10.4).</summary>
public enum FakeProviderError
{
    /// <summary>Throws <see cref="TimeoutException"/>.</summary>
    Timeout,

    /// <summary>Throws <see cref="HttpRequestException"/> with status 429.</summary>
    RateLimited,

    /// <summary>Throws <see cref="TaskCanceledException"/>, as <see cref="HttpClient"/> does when its own timeout expires.</summary>
    HttpTimeout,
}

/// <summary>
/// A deterministic <see cref="IMetadataProvider"/> over JSON fixtures (SPEC §10.4). Search matches when every
/// query word is a prefix of a word in the name, ignoring case, accents and punctuation. Year does not filter,
/// so callers can score near misses; it only orders results. Every call is recorded, and errors can be queued.
/// </summary>
public sealed class FakeMetadataProvider : IMetadataProvider
{
    private readonly Lock _gate = new();
    private readonly Dictionary<ExternalId, MetadataFixture> _titles;
    private readonly Queue<FakeProviderError> _errors = new();
    private readonly List<TitleQuery> _searchQueries = [];
    private readonly List<ExternalId> _titleRequests = [];
    private readonly List<(ExternalId ShowId, int Season)> _seasonRequests = [];

    /// <summary>Loads every <c>*.json</c> fixture in <paramref name="fixtureDirectory"/> whose id belongs to <paramref name="key"/>.</summary>
    public FakeMetadataProvider(string? fixtureDirectory = null, string key = ExternalProviders.Tmdb)
    {
        Key = key;
        var directory = fixtureDirectory ?? DefaultFixtureDirectory;
        _titles = Directory.EnumerateFiles(directory, "*.json")
            .Order(StringComparer.Ordinal)
            .Select(file => MetadataFixture.Parse(File.ReadAllText(file)))
            .Where(f => f.ExternalId.Provider == key)
            .ToDictionary(f => f.ExternalId);
    }

    /// <summary>The fixtures copied next to the test assembly from <c>server/tests/fixtures/metadata</c>.</summary>
    public static string DefaultFixtureDirectory => Path.Combine(AppContext.BaseDirectory, "fixtures", "metadata");

    public string Key { get; }

    public IReadOnlyCollection<MetadataFixture> Titles => _titles.Values;

    public IReadOnlyList<TitleQuery> SearchQueries
    {
        get
        {
            lock (_gate)
            {
                return [.. _searchQueries];
            }
        }
    }

    public IReadOnlyList<ExternalId> TitleRequests
    {
        get
        {
            lock (_gate)
            {
                return [.. _titleRequests];
            }
        }
    }

    public IReadOnlyList<(ExternalId ShowId, int Season)> SeasonRequests
    {
        get
        {
            lock (_gate)
            {
                return [.. _seasonRequests];
            }
        }
    }

    /// <summary>Makes the next <paramref name="calls"/> calls, of any kind, fail with <paramref name="error"/>.</summary>
    public void FailNext(FakeProviderError error, int calls = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(calls);
        lock (_gate)
        {
            for (var i = 0; i < calls; i++)
            {
                _errors.Enqueue(error);
            }
        }
    }

    public Task<IReadOnlyList<TitleCandidate>> SearchAsync(TitleQuery q, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(q);
        ct.ThrowIfCancellationRequested();
        lock (_gate)
        {
            _searchQueries.Add(q);
            ThrowQueuedError();
        }

        var queryWords = Words(q.Name);
        if (queryWords.Length == 0)
        {
            return Task.FromResult<IReadOnlyList<TitleCandidate>>([]);
        }

        var normalizedQuery = string.Join(' ', queryWords);
        IReadOnlyList<TitleCandidate> results = [.. _titles.Values
            .Where(t => q.Kind is null || t.Kind == q.Kind)
            .Select(t => (Title: t, Words: Words(t.Name)))
            .Where(t => queryWords.All(qw => t.Words.Any(w => w.StartsWith(qw, StringComparison.Ordinal))))
            .OrderBy(t => string.Join(' ', t.Words) == normalizedQuery ? 0 : 1)
            .ThenBy(t => q.Year is { } year && t.Title.Year is { } titleYear ? Math.Abs(year - titleYear) : int.MaxValue)
            .ThenBy(t => t.Title.Id, StringComparer.Ordinal)
            .Select(t => t.Title.ToCandidate())];
        return Task.FromResult(results);
    }

    public Task<TitleDetails> GetTitleAsync(ExternalId id, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_gate)
        {
            _titleRequests.Add(id);
            ThrowQueuedError();
        }

        return Task.FromResult(Find(id).ToDetails());
    }

    public Task<SeasonDetails> GetSeasonAsync(ExternalId showId, int season, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_gate)
        {
            _seasonRequests.Add((showId, season));
            ThrowQueuedError();
        }

        var details = Find(showId).Seasons.FirstOrDefault(s => s.Number == season)
            ?? throw new KeyNotFoundException($"No fixture season {season} for '{showId}'.");
        return Task.FromResult(details.ToDetails(showId));
    }

    private MetadataFixture Find(ExternalId id) =>
        _titles.TryGetValue(id, out var title) ? title : throw new KeyNotFoundException($"No metadata fixture for '{id}'.");

    private void ThrowQueuedError()
    {
        if (!_errors.TryDequeue(out var error))
        {
            return;
        }

        throw error switch
        {
            FakeProviderError.Timeout => new TimeoutException($"Fake provider '{Key}' timed out."),
            FakeProviderError.RateLimited => new HttpRequestException(
                $"Fake provider '{Key}' is rate limited.", null, HttpStatusCode.TooManyRequests),
            FakeProviderError.HttpTimeout => new TaskCanceledException($"Fake provider '{Key}' timed out (HttpClient)."),
            _ => new InvalidOperationException($"Unknown fake error {error}."),
        };
    }

    /// <summary>Lower-cased words with accents and punctuation removed: "Amélie!" → ["amelie"].</summary>
    private static string[] Words(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark || c == '\'')
            {
                continue;
            }

            builder.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');
        }

        return builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }
}
