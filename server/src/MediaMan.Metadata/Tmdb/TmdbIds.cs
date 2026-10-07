using System.Globalization;
using MediaMan.Core;
using MediaMan.Core.Domain;

namespace MediaMan.Metadata.Tmdb;

/// <summary>
/// TMDB movie and TV ids overlap, so a TMDB <see cref="ExternalId"/> carries the kind in its value:
/// <c>tmdb:movie/603</c> or <c>tmdb:tv/1396</c> (ADR 0006).
/// </summary>
public static class TmdbIds
{
    private const string MoviePrefix = "movie/";
    private const string ShowPrefix = "tv/";

    public static ExternalId Create(TitleKind kind, int id) =>
        new(ExternalProviders.Tmdb, (kind == TitleKind.Show ? ShowPrefix : MoviePrefix) + id.ToString(CultureInfo.InvariantCulture));

    public static bool TryParse(ExternalId id, out TitleKind kind, out int number)
    {
        kind = default;
        number = 0;
        if (id.Provider != ExternalProviders.Tmdb)
        {
            return false;
        }

        string digits;
        if (id.Value.StartsWith(MoviePrefix, StringComparison.Ordinal))
        {
            kind = TitleKind.Movie;
            digits = id.Value[MoviePrefix.Length..];
        }
        else if (id.Value.StartsWith(ShowPrefix, StringComparison.Ordinal))
        {
            kind = TitleKind.Show;
            digits = id.Value[ShowPrefix.Length..];
        }
        else
        {
            return false;
        }

        return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out number) && number > 0;
    }

    public static (TitleKind Kind, int Number) Parse(ExternalId id) =>
        TryParse(id, out var kind, out var number)
            ? (kind, number)
            : throw new ArgumentException($"'{id}' is not a TMDB id like 'tmdb:movie/603' or 'tmdb:tv/1396'.", nameof(id));
}
