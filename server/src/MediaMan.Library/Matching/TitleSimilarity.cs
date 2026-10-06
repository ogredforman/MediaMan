using System.Globalization;
using System.Text;

namespace MediaMan.Library.Matching;

/// <summary>Compares a parsed title with a provider title, ignoring case, accents and punctuation (pure).</summary>
public static class TitleSimilarity
{
    /// <summary>Lower-cased words joined by single spaces, with accents and punctuation removed and <c>&amp;</c> read as <c>and</c>.</summary>
    public static string Normalize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var builder = new StringBuilder(text.Length);
        foreach (var c in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark || c is '\'' or '’')
            {
                continue;
            }

            if (c == '&')
            {
                builder.Append(" and ");
            }
            else
            {
                builder.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');
            }
        }

        return string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>1 for identical normalized titles, falling towards 0 with edit distance relative to the longer title.</summary>
    public static double Score(string a, string b)
    {
        var x = Normalize(a);
        var y = Normalize(b);
        if (x.Length == 0 || y.Length == 0)
        {
            return 0;
        }

        return 1.0 - ((double)Levenshtein(x, y) / Math.Max(x.Length, y.Length));
    }

    private static int Levenshtein(string a, string b)
    {
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
        {
            previous[j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }
}
