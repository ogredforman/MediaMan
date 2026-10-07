namespace MediaMan.Metadata.Tmdb;

/// <summary>How <see cref="TmdbMetadataProvider"/> reaches TMDB (SPEC §8 metadata [M]).</summary>
public sealed class TmdbOptions
{
    /// <summary>
    /// The read-access token shipped with MediaMan. Anything in a distributed build can be extracted, so it is
    /// committed like Kodi's and Jellyfin's; it is still a secret for logging purposes (SPEC §9) and never goes in a URL.
    /// </summary>
    public const string ShippedAccessToken =
        "eyJhbGciOiJIUzI1NiJ9.eyJhdWQiOiJmMmJmYmRlYjY0MTQ5Y2JhYmUwMTg4ZDYxOTg4YzExMSIsIm5iZiI6MTc5MTM1ODczMi44NTYsInN1YiI6IjZhYzVmNzBjZDY5Yzk3YjllMzJmNTJjYyIsInNjb3BlcyI6WyJhcGlfcmVhZCJdLCJ2ZXJzaW9uIjoxfQ.zo_kuO9h2iEvKpJHkeLa_zSBsXf6-ZkKsPcnrs6duno";

    /// <summary>A TMDB v4 read-access token that overrides the shipped one; empty uses <see cref="ShippedAccessToken"/>.</summary>
    public string? AccessToken { get; init; }

    public string EffectiveAccessToken =>
        string.IsNullOrWhiteSpace(AccessToken) ? ShippedAccessToken : AccessToken.Trim();

    public Uri BaseUrl { get; init; } = new("https://api.themoviedb.org/3/");

    /// <summary>Prefix for TMDB image paths; 2.2 caches the images locally.</summary>
    public string ImageBaseUrl { get; init; } = "https://image.tmdb.org/t/p/original";

    /// <summary>Language for names, overviews and artwork, as a TMDB language tag.</summary>
    public string Language { get; init; } = "en-US";

    /// <summary>Where responses are cached, one file per request. Kept under the data directory.</summary>
    public required string CacheDirectory { get; init; }

    public TimeSpan CacheLifetime { get; init; } = TimeSpan.FromDays(7);

    /// <summary>Waits between retries of a rate-limited (429) request when TMDB sends no <c>Retry-After</c>.</summary>
    public IReadOnlyList<TimeSpan> RetryDelays { get; init; } =
        [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4)];

    /// <summary>Upper bound on a <c>Retry-After</c> wait, so a hostile or broken header can't stall a scan.</summary>
    public TimeSpan MaxRetryAfter { get; init; } = TimeSpan.FromSeconds(30);
}
