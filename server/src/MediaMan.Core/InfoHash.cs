using System.Diagnostics.CodeAnalysis;

namespace MediaMan.Core;

/// <summary>
/// A BitTorrent v1 info hash (20-byte SHA-1). Accepts 40-character hex or 32-character base32
/// (as found in magnet links) and normalises to lowercase hex.
/// </summary>
public readonly record struct InfoHash
{
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    private InfoHash(string hex) => Hex = hex;

    /// <summary>Lowercase 40-character hex.</summary>
    public string Hex { get; }

    public static InfoHash Parse(string text) =>
        TryParse(text, out var hash) ? hash : throw new FormatException($"'{text}' is not a 40-character hex or 32-character base32 info hash.");

    public static bool TryParse([NotNullWhen(true)] string? text, out InfoHash hash)
    {
        hash = default;
        if (text is null)
        {
            return false;
        }

        if (text.Length == 40 && text.All(char.IsAsciiHexDigit))
        {
            hash = new InfoHash(text.ToLowerInvariant());
            return true;
        }

        if (text.Length == 32 && TryDecodeBase32(text, out var bytes))
        {
            hash = new InfoHash(Convert.ToHexStringLower(bytes));
            return true;
        }

        return false;
    }

    public override string ToString() => Hex ?? string.Empty;

    private static bool TryDecodeBase32(string text, out byte[] bytes)
    {
        // 32 base32 chars = 160 bits = exactly 20 bytes, so no padding handling is needed.
        bytes = new byte[20];
        var buffer = 0;
        var bits = 0;
        var index = 0;
        foreach (var c in text)
        {
            var value = Base32Alphabet.IndexOf(char.ToUpperInvariant(c), StringComparison.Ordinal);
            if (value < 0)
            {
                return false;
            }

            buffer = (buffer << 5) | value;
            bits += 5;
            if (bits >= 8)
            {
                bits -= 8;
                bytes[index++] = (byte)(buffer >> bits);
                buffer &= (1 << bits) - 1;
            }
        }

        return true;
    }
}
