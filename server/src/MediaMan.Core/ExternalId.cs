using System.Diagnostics.CodeAnalysis;

namespace MediaMan.Core;

/// <summary>Well-known metadata provider keys used in <see cref="ExternalId"/>.</summary>
public static class ExternalProviders
{
    public const string Tmdb = "tmdb";
    public const string Imdb = "imdb";
    public const string Tvdb = "tvdb";
    public const string AniList = "anilist";
}

/// <summary>An id at an external metadata provider, written as <c>provider:value</c> (e.g. <c>tmdb:603</c>).</summary>
public readonly record struct ExternalId
{
    public ExternalId(string provider, string value)
    {
        if (!IsValidProvider(provider))
        {
            throw new ArgumentException($"Provider '{provider}' must be non-empty with no ':' or whitespace.", nameof(provider));
        }

        if (string.IsNullOrWhiteSpace(value) || value.Trim() != value)
        {
            throw new ArgumentException("Value must be non-empty with no leading or trailing whitespace.", nameof(value));
        }

        Provider = provider.ToLowerInvariant();
        Value = value;
    }

    public string Provider { get; }

    public string Value { get; }

    public static ExternalId Parse(string text) =>
        TryParse(text, out var id) ? id : throw new FormatException($"'{text}' is not an external id like 'tmdb:603'.");

    public static bool TryParse([NotNullWhen(true)] string? text, out ExternalId id)
    {
        id = default;
        if (text is null)
        {
            return false;
        }

        var colon = text.IndexOf(':', StringComparison.Ordinal);
        if (colon <= 0)
        {
            return false;
        }

        var provider = text[..colon];
        var value = text[(colon + 1)..];
        if (!IsValidProvider(provider) || string.IsNullOrWhiteSpace(value) || value.Trim() != value)
        {
            return false;
        }

        id = new ExternalId(provider, value);
        return true;
    }

    public override string ToString() => Provider is null ? string.Empty : $"{Provider}:{Value}";

    private static bool IsValidProvider(string? provider) =>
        !string.IsNullOrEmpty(provider) && !provider.Any(c => c == ':' || char.IsWhiteSpace(c));
}
