using System.Globalization;
using System.Text;

namespace MediaMan.Server.LibraryApi;

/// <summary>How library search matches and ranks a title's name against what the user typed (SPEC §8 browsing). Pure.</summary>
public static class SearchRules
{
    public const int MaxQueryLength = 200;
    public const int DefaultLimit = 20;
    public const int MaxLimit = 50;

    /// <summary>
    /// Lower-case, accents stripped, apostrophes dropped, other punctuation as spaces, whitespace collapsed. Only the
    /// combining diacritics block (U+0300–U+036F) counts as accents: other scripts' marks (Japanese dakuten, Indic vowel
    /// signs) change the letter, so they stay.
    /// </summary>
    public static string Normalize(string text)
    {
        var builder = new StringBuilder(text.Length);
        var pendingSpace = false;
        foreach (var c in text.Normalize(NormalizationForm.FormD))
        {
            if (c is >= '̀' and <= 'ͯ' or '\'' or '’')
            {
                continue;
            }

            if (char.IsLetterOrDigit(c) || IsMark(c))
            {
                if (pendingSpace && builder.Length > 0)
                {
                    builder.Append(' ');
                }

                pendingSpace = false;
                builder.Append(char.ToLowerInvariant(c));
            }
            else
            {
                pendingSpace = true;
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private static bool IsMark(char c) => CharUnicodeInfo.GetUnicodeCategory(c)
        is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark;

    /// <summary>A normalized query, split once so it can be matched against many names.</summary>
    public sealed record Query(string Text, IReadOnlyList<string> Words)
    {
        public static Query Of(string raw)
        {
            var text = Normalize(raw);
            return new Query(text, text.Length == 0 ? [] : text.Split(' '));
        }

        public bool IsBlank => Text.Length == 0;
    }

    /// <summary>
    /// The rank of a name for the query, lower is better, or null when it doesn't match: 0 the exact name, 1 the name
    /// starts with the query, 2 every query word starts a word of the name, 3 the name contains the query (spaces ignored,
    /// so "spiderman" finds "Spider-Man").
    /// </summary>
    public static int? Rank(Query query, string name)
    {
        if (query.IsBlank)
        {
            return null;
        }

        var normalized = Normalize(name);
        if (normalized == query.Text)
        {
            return 0;
        }

        if (normalized.StartsWith(query.Text, StringComparison.Ordinal))
        {
            return 1;
        }

        var words = normalized.Split(' ');
        if (query.Words.All(q => words.Any(w => w.StartsWith(q, StringComparison.Ordinal))))
        {
            return 2;
        }

        return normalized.Replace(" ", "", StringComparison.Ordinal)
            .Contains(query.Text.Replace(" ", "", StringComparison.Ordinal), StringComparison.Ordinal)
            ? 3
            : null;
    }
}
