using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using MediaMan.Core;
using MediaMan.Core.Domain;
using MediaMan.Core.Modules;

namespace MediaMan.Metadata.Tmdb;

/// <summary>
/// The real TMDB <see cref="IMetadataProvider"/> (SPEC §8 metadata [M]) over the v3 API with a v4 read-access token.
/// Successful responses are cached on disk for <see cref="TmdbOptions.CacheLifetime"/>; a rate-limited request waits
/// (honouring <c>Retry-After</c>) and retries, then fails with <see cref="HttpRequestException"/> 429 for the caller's
/// own retry. An unknown id throws <see cref="KeyNotFoundException"/>. Takes ownership of <paramref name="http"/>.
/// </summary>
public sealed class TmdbMetadataProvider(
    HttpClient http,
    TmdbOptions options,
    IClock clock,
    Func<TimeSpan, CancellationToken, Task>? delay = null) : IMetadataProvider, IDisposable
{
    private static readonly JsonSerializerOptions _json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    private readonly TmdbResponseCache _cache = new(options.CacheDirectory, options.CacheLifetime, clock);
    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? Task.Delay;

    public string Key => ExternalProviders.Tmdb;

    public async Task<IReadOnlyList<TitleCandidate>> SearchAsync(TitleQuery q, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(q);
        if (string.IsNullOrWhiteSpace(q.Name))
        {
            return [];
        }

        var movies = q.Kind is null or TitleKind.Movie ? await SearchKindAsync(TitleKind.Movie, q, ct) : [];
        var shows = q.Kind is null or TitleKind.Show ? await SearchKindAsync(TitleKind.Show, q, ct) : [];

        // With no kind, alternate the two lists so neither crowds out the other's best matches.
        var merged = new List<TitleCandidate>(movies.Count + shows.Count);
        for (var i = 0; i < Math.Max(movies.Count, shows.Count); i++)
        {
            if (i < movies.Count)
            {
                merged.Add(movies[i]);
            }

            if (i < shows.Count)
            {
                merged.Add(shows[i]);
            }
        }

        return merged;
    }

    public async Task<TitleDetails> GetTitleAsync(ExternalId id, CancellationToken ct)
    {
        var (kind, number) = TmdbIds.Parse(id);
        var path = kind == TitleKind.Show ? "tv" : "movie";
        var title = await GetAsync<TmdbTitle>(
            $"{path}/{number}?append_to_response=external_ids,images&include_image_language={ImageLanguages}&language={Language}",
            $"title '{id}'",
            ct);

        var externalIds = new List<ExternalId>();
        if (!string.IsNullOrWhiteSpace(title.ExternalIds?.ImdbId))
        {
            externalIds.Add(new ExternalId(ExternalProviders.Imdb, title.ExternalIds.ImdbId.Trim()));
        }

        if (title.ExternalIds?.TvdbId is > 0 and var tvdb)
        {
            externalIds.Add(new ExternalId(ExternalProviders.Tvdb, tvdb.ToString(CultureInfo.InvariantCulture)));
        }

        var runtime = kind == TitleKind.Show ? (title.EpisodeRunTime is [var first, ..] ? first : null) : title.Runtime;
        return new TitleDetails
        {
            Id = TmdbIds.Create(kind, number),
            Kind = kind,
            Name = (kind == TitleKind.Show ? title.Name : title.Title) ?? id.ToString(),
            Year = YearOf(kind == TitleKind.Show ? title.FirstAirDate : title.ReleaseDate),
            Overview = TextOrNull(title.Overview),
            Genres = title.Genres?.Select(g => g.Name).ToList() ?? [],
            Rating = title.VoteCount > 0 ? Math.Round(title.VoteAverage, 1) : null,
            RuntimeMinutes = runtime is > 0 ? runtime : null,
            ExternalIds = externalIds,
            Seasons = title.Seasons?.Select(s => new SeasonSummary(s.SeasonNumber, TextOrNull(s.Name), s.EpisodeCount)).ToList() ?? [],
            Artwork = ArtworkOf(title),
        };
    }

    public async Task<SeasonDetails> GetSeasonAsync(ExternalId showId, int season, CancellationToken ct)
    {
        var (kind, number) = TmdbIds.Parse(showId);
        if (kind != TitleKind.Show)
        {
            throw new ArgumentException($"'{showId}' is a movie, which has no seasons.", nameof(showId));
        }

        var details = await GetAsync<TmdbSeason>(
            $"tv/{number}/season/{season.ToString(CultureInfo.InvariantCulture)}?language={Language}",
            $"season {season} of '{showId}'",
            ct);

        return new SeasonDetails
        {
            ShowId = TmdbIds.Create(TitleKind.Show, number),
            Number = details.SeasonNumber,
            Name = TextOrNull(details.Name),
            Overview = TextOrNull(details.Overview),
            PosterUrl = ImageUrl(details.PosterPath),
            Episodes = details.Episodes?
                .Select(e => new EpisodeDetails(
                    e.EpisodeNumber,
                    null, // TMDB has no absolute numbers; the importer counts them over the regular seasons.
                    TextOrNull(e.Name),
                    TextOrNull(e.Overview),
                    DateOnly.TryParseExact(e.AirDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var aired) ? aired : null,
                    ImageUrl(e.StillPath)))
                .ToList() ?? [],
        };
    }

    public void Dispose() => http.Dispose();

    private string Language => Uri.EscapeDataString(options.Language);

    // Artwork in the configured language first, then artwork with no text.
    private string ImageLanguages => Uri.EscapeDataString(options.Language.Split('-')[0]) + ",null";

    private async Task<List<TitleCandidate>> SearchKindAsync(TitleKind kind, TitleQuery q, CancellationToken ct)
    {
        var path = kind == TitleKind.Show ? "search/tv" : "search/movie";
        var yearParameter = kind == TitleKind.Show ? "first_air_date_year" : "year";
        var request = $"{path}?query={Uri.EscapeDataString(q.Name.Trim())}&include_adult=false&language={Language}&page=1";

        // TMDB filters strictly by year, so the year-filtered results lead and the unfiltered ones follow: a file
        // whose year is off by one still finds its title, and the matcher scores the difference (SPEC §7.2).
        var pages = new List<TmdbSearchPage>();
        if (q.Year is { } year)
        {
            pages.Add(await GetAsync<TmdbSearchPage>($"{request}&{yearParameter}={year.ToString(CultureInfo.InvariantCulture)}", "search", ct));
        }

        pages.Add(await GetAsync<TmdbSearchPage>(request, "search", ct));

        var seen = new HashSet<int>();
        return pages
            .SelectMany(p => p.Results)
            .Where(r => seen.Add(r.Id))
            .Select(r => new TitleCandidate(
                TmdbIds.Create(kind, r.Id),
                kind,
                (kind == TitleKind.Show ? r.Name : r.Title) ?? string.Empty,
                YearOf(kind == TitleKind.Show ? r.FirstAirDate : r.ReleaseDate),
                TextOrNull(r.Overview),
                ImageUrl(r.PosterPath)))
            .Where(c => c.Name.Length > 0)
            .ToList();
    }

    private List<ArtworkRef> ArtworkOf(TmdbTitle title)
    {
        var artwork = new List<ArtworkRef>();
        if (ImageUrl(title.PosterPath) is { } poster)
        {
            artwork.Add(new ArtworkRef(ArtworkType.Poster, poster, LanguageOf(title.Images?.Posters, title.PosterPath)));
        }

        if (ImageUrl(title.BackdropPath) is { } backdrop)
        {
            artwork.Add(new ArtworkRef(ArtworkType.Backdrop, backdrop, LanguageOf(title.Images?.Backdrops, title.BackdropPath)));
        }

        // TMDB returns logos best-voted first; prefer one in the configured language over a textless one.
        var language = options.Language.Split('-')[0];
        var logo = title.Images?.Logos?.FirstOrDefault(l => l.Iso6391 == language) ?? (title.Images?.Logos is [var best, ..] ? best : null);
        if (logo is not null && ImageUrl(logo.FilePath) is { } logoUrl)
        {
            artwork.Add(new ArtworkRef(ArtworkType.Logo, logoUrl, TextOrNull(logo.Iso6391)));
        }

        return artwork;
    }

    private static string? LanguageOf(IReadOnlyList<TmdbImage>? images, string? path) =>
        TextOrNull(images?.FirstOrDefault(i => i.FilePath == path)?.Iso6391);

    private string? ImageUrl(string? path) =>
        string.IsNullOrWhiteSpace(path) ? null : options.ImageBaseUrl.TrimEnd('/') + "/" + path.TrimStart('/');

    private static int? YearOf(string? date) =>
        date is { Length: >= 4 } && int.TryParse(date.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var year)
            ? year
            : null;

    private static string? TextOrNull(string? text) => string.IsNullOrWhiteSpace(text) ? null : text;

    private async Task<T> GetAsync<T>(string request, string what, CancellationToken ct)
        where T : class
    {
        if (await _cache.TryReadAsync(request, ct) is { } cached && TryDeserialize<T>(cached, out var hit))
        {
            return hit;
        }

        // Only a body that parses is cached, so a garbled response can't stick for the whole cache lifetime.
        var body = await FetchAsync(request, what, ct);
        if (!TryDeserialize<T>(body, out var value))
        {
            throw new HttpRequestException($"TMDB returned an unreadable response for {what}; it will be retried.");
        }

        await _cache.WriteAsync(request, body, ct);
        return value;
    }

    private static bool TryDeserialize<T>(string body, [NotNullWhen(true)] out T? value)
        where T : class
    {
        try
        {
            value = JsonSerializer.Deserialize<T>(body, _json);
        }
        catch (JsonException)
        {
            value = null;
        }

        return value is not null;
    }

    private async Task<string> FetchAsync(string request, string what, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var message = new HttpRequestMessage(HttpMethod.Get, new Uri(options.BaseUrl, request));
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.EffectiveAccessToken);
            message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var response = await http.SendAsync(message, ct);

            switch (response.StatusCode)
            {
                case HttpStatusCode.OK:
                    return await response.Content.ReadAsStringAsync(ct);
                case HttpStatusCode.NotFound:
                    throw new KeyNotFoundException($"TMDB has no {what}.");
                case HttpStatusCode.Unauthorized:
                    throw new HttpRequestException(
                        "TMDB rejected the access token. Set a valid TMDB read-access token in MediaMan:Metadata:Tmdb:AccessToken.",
                        null,
                        response.StatusCode);
                case HttpStatusCode.TooManyRequests when attempt < options.RetryDelays.Count:
                    await _delay(RetryWait(response, attempt), ct);
                    continue;
                case HttpStatusCode.TooManyRequests:
                    throw new HttpRequestException(
                        "TMDB is limiting requests; matching resumes on its own shortly.", null, response.StatusCode);
                default:
                    throw new HttpRequestException(
                        $"TMDB answered {(int)response.StatusCode} for {what}; it will be retried.", null, response.StatusCode);
            }
        }
    }

    private TimeSpan RetryWait(HttpResponseMessage response, int attempt)
    {
        var retryAfter = response.Headers.RetryAfter;
        var wait = retryAfter?.Delta
            ?? (retryAfter?.Date is { } date ? date - clock.UtcNow : options.RetryDelays[attempt]);
        return wait < TimeSpan.Zero ? TimeSpan.Zero : wait > options.MaxRetryAfter ? options.MaxRetryAfter : wait;
    }
}
