using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using MediaMan.Server.Hosting;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.WebUtilities;

namespace MediaMan.Server.LibraryApi;

/// <summary>One page of a list operation: skip <see cref="Offset"/> items, return up to <see cref="Limit"/>.</summary>
public readonly record struct PageRequest(int Offset, int Limit);

/// <summary>
/// The contract's <c>limit</c>/<c>cursor</c> paging. Cursors are opaque to clients; today they carry an offset,
/// so items added while paging can shift a page. Switch to a keyset cursor if that starts to matter.
/// </summary>
public static class Paging
{
    public const int DefaultLimit = 50;
    public const int MaxLimit = 200;
    public const string InvalidCode = "pagination.invalid";

    private const string OffsetPrefix = "o:";

    public static bool TryRead(int? limit, string? cursor, out PageRequest page, [NotNullWhen(false)] out ProblemHttpResult? problem)
    {
        page = default;
        problem = null;
        var size = limit ?? DefaultLimit;
        if (size is < 1 or > MaxLimit)
        {
            problem = Invalid($"limit must be between 1 and {MaxLimit}.");
            return false;
        }

        var offset = 0;
        if (cursor is not null && !TryDecode(cursor, out offset))
        {
            problem = Invalid("The cursor isn't one this server issued, or it has expired.");
            return false;
        }

        page = new PageRequest(offset, size);
        return true;
    }

    /// <summary>
    /// Trims rows fetched with <c>Take(Limit + 1)</c> to the page and returns the next page's cursor, if there is one.
    /// </summary>
    public static (IReadOnlyList<T> Items, PageInfo Info) Page<T>(IReadOnlyList<T> rows, PageRequest page)
    {
        ArgumentNullException.ThrowIfNull(rows);
        return rows.Count > page.Limit
            ? ([.. rows.Take(page.Limit)], new PageInfo(Encode(page.Offset + page.Limit)))
            : (rows, new PageInfo(null));
    }

    private static string Encode(int offset) =>
        WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(OffsetPrefix + offset.ToString(CultureInfo.InvariantCulture)));

    private static bool TryDecode(string cursor, out int offset)
    {
        offset = 0;
        string text;
        try
        {
            text = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(cursor));
        }
        catch (FormatException)
        {
            return false;
        }

        return text.StartsWith(OffsetPrefix, StringComparison.Ordinal)
            && int.TryParse(text.AsSpan(OffsetPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out offset);
    }

    private static ProblemHttpResult Invalid(string detail) =>
        Problems.Result(StatusCodes.Status400BadRequest, InvalidCode, "Invalid page request.", detail,
            "Start again from the first page, without a cursor.");
}
