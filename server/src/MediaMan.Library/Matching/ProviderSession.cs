using MediaMan.Core;
using MediaMan.Core.Modules;

namespace MediaMan.Library.Matching;

/// <summary>
/// Per-run provider access shared by the matcher and the importer: caches searches, titles and seasons, retries
/// errors with backoff (SPEC §7.2 step 7), and trips when they persist so the rest of the run stops calling.
/// </summary>
internal sealed class ProviderSession(IMetadataProvider provider, MatcherOptions options)
{
    private readonly Dictionary<TitleQuery, IReadOnlyList<TitleCandidate>> _searches = [];
    private readonly Dictionary<ExternalId, TitleDetails> _titles = [];
    private readonly Dictionary<(ExternalId, int), SeasonDetails> _seasons = [];

    public bool ProviderDown { get; private set; }

    public async Task<IReadOnlyList<TitleCandidate>> SearchAsync(TitleQuery query, CancellationToken ct)
    {
        if (!_searches.TryGetValue(query, out var results))
        {
            results = await CallAsync(c => provider.SearchAsync(query, c), ct);
            _searches[query] = results;
        }

        return results;
    }

    public async Task<TitleDetails> GetTitleAsync(ExternalId id, CancellationToken ct)
    {
        if (!_titles.TryGetValue(id, out var details))
        {
            details = await CallAsync(c => provider.GetTitleAsync(id, c), ct);
            _titles[id] = details;
        }

        return details;
    }

    public async Task<SeasonDetails> GetSeasonAsync(ExternalId showId, int season, CancellationToken ct)
    {
        if (!_seasons.TryGetValue((showId, season), out var details))
        {
            details = await CallAsync(c => provider.GetSeasonAsync(showId, season, c), ct);
            _seasons[(showId, season)] = details;
        }

        return details;
    }

    private async Task<T> CallAsync<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await call(ct);
            }
            catch (Exception e) when (IsProviderError(e, ct) && attempt < options.RetryDelays.Count)
            {
                await Task.Delay(options.RetryDelays[attempt], ct);
            }
            catch (Exception e) when (IsProviderError(e, ct))
            {
                ProviderDown = true;
                throw new ProviderUnavailableException(e);
            }
        }
    }

    // HttpClient reports its own timeout as a cancellation the caller didn't ask for.
    private static bool IsProviderError(Exception e, CancellationToken ct) =>
        e is TimeoutException or HttpRequestException || (e is OperationCanceledException && !ct.IsCancellationRequested);
}

/// <summary>The provider kept failing after every retry; the affected files stay Pending for the next scan.</summary>
internal sealed class ProviderUnavailableException(Exception inner)
    : Exception("The metadata provider kept failing; matching resumes on the next scan.", inner);
