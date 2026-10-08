using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using MediaMan.Core;

namespace MediaMan.Auth;

/// <summary>
/// Signs URLs that clients load without a bearer token, such as artwork in <c>&lt;img&gt;</c> tags (SPEC §9, ADR 0008):
/// HMAC-SHA256 of the path and expiry on the install key's <see cref="InstallKey.ArtworkUrls"/> subkey, not bound to a
/// user. The expiry is the next UTC midnight plus one day, so a path's signed URL stays the same all day and browsers
/// can cache what it loads. Every time comes from <see cref="IClock"/>.
/// </summary>
public sealed class UrlSigner
{
    public const string ExpiresParameter = "exp";
    public const string SignatureParameter = "sig";

    private readonly byte[] _key;
    private readonly IClock _clock;

    /// <param name="signingKey">The derived subkey; copied, so the caller may zero its array.</param>
    public UrlSigner(ReadOnlySpan<byte> signingKey, IClock clock)
    {
        if (signingKey.Length < InstallKey.SizeBytes)
        {
            throw new ArgumentException($"The signing key must be at least {InstallKey.SizeBytes} bytes.", nameof(signingKey));
        }

        _key = signingKey.ToArray();
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    /// <summary>The path with <c>?exp=…&amp;sig=…</c> appended. The path must have no query of its own.</summary>
    public string Sign(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        if (path.Contains('?', StringComparison.Ordinal))
        {
            throw new ArgumentException("The path must not have a query.", nameof(path));
        }

        var expires = new DateTimeOffset(_clock.UtcNow.UtcDateTime.Date, TimeSpan.Zero).AddDays(2).ToUnixTimeSeconds();
        return $"{path}?{ExpiresParameter}={expires.ToString(CultureInfo.InvariantCulture)}&{SignatureParameter}={Signature(path, expires)}";
    }

    /// <summary>Whether <paramref name="signature"/> was made by <see cref="Sign"/> for this path and expiry, and hasn't expired.</summary>
    public bool IsValid(string path, string? expires, string? signature)
    {
        if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(signature)
            || !long.TryParse(expires, NumberStyles.None, CultureInfo.InvariantCulture, out var exp)
            || exp <= _clock.UtcNow.ToUnixTimeSeconds())
        {
            return false;
        }

        // Compared as bytes in constant time, so the signature can't be guessed a character at a time.
        var expected = Encoding.ASCII.GetBytes(Signature(path, exp));
        var actual = Encoding.UTF8.GetBytes(signature);
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    private string Signature(string path, long expires)
    {
        var mac = HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes($"{path}\n{expires.ToString(CultureInfo.InvariantCulture)}"));
        return Convert.ToBase64String(mac).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
