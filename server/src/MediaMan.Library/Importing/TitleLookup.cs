using MediaMan.Core;
using MediaMan.Core.Domain;

namespace MediaMan.Library.Importing;

internal static class TitleLookup
{
    /// <summary>The stored titles carrying <paramref name="id"/> among their external ids.</summary>
    public static IQueryable<Title> WithExternalId(this IQueryable<Title> titles, ExternalId id) => id.Provider switch
    {
        ExternalProviders.Tmdb => titles.Where(t => t.ExternalIds.Tmdb == id.Value),
        ExternalProviders.AniList => titles.Where(t => t.ExternalIds.AniList == id.Value),
        ExternalProviders.Tvdb => titles.Where(t => t.ExternalIds.Tvdb == id.Value),
        ExternalProviders.Imdb => titles.Where(t => t.ExternalIds.Imdb == id.Value),
        _ => throw new NotSupportedException($"Titles can't be keyed by provider '{id.Provider}'."),
    };
}
