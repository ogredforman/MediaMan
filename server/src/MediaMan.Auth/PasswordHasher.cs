using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace MediaMan.Auth;

/// <summary>Argon2id cost parameters: memory in KiB, iterations and lanes.</summary>
public sealed record Argon2Parameters(int MemoryKib, int Iterations, int Parallelism);

/// <summary>
/// Argon2id password hashing (SPEC §9, ADR 0008). Hashes are PHC strings,
/// <c>$argon2id$v=19$m=…,t=…,p=…$salt$hash</c>, so each carries its own parameters and they can be raised later
/// without breaking existing passwords.
/// </summary>
public sealed class PasswordHasher(Argon2Parameters parameters)
{
    private const int SaltBytes = 16;
    private const int HashBytes = 32;
    private const string Prefix = "$argon2id$v=19$";

    /// <summary>Longer passwords are refused, so a huge request body can't become a huge allocation.</summary>
    public const int MaximumPasswordLength = 1024;

    /// <summary>OWASP's recommended minimum: 19 MiB, 2 iterations, 1 lane.</summary>
    public static PasswordHasher Default { get; } = new(new Argon2Parameters(19 * 1024, 2, 1));

    public Argon2Parameters Parameters { get; } = parameters ?? throw new ArgumentNullException(nameof(parameters));

    /// <exception cref="ArgumentException">The password is empty or longer than <see cref="MaximumPasswordLength"/>.</exception>
    public string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(password.Length, MaximumPasswordLength);

        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Compute(password, salt, Parameters);
        return string.Create(CultureInfo.InvariantCulture,
            $"{Prefix}m={Parameters.MemoryKib},t={Parameters.Iterations},p={Parameters.Parallelism}${Encode(salt)}${Encode(hash)}");
    }

    /// <summary>True when <paramref name="password"/> matches <paramref name="encodedHash"/>. A malformed hash never matches.</summary>
    public static bool Verify(string password, string encodedHash)
    {
        ArgumentNullException.ThrowIfNull(password);

        if (password.Length is 0 or > MaximumPasswordLength || !TryParse(encodedHash, out var parameters, out var salt, out var expected))
        {
            return false;
        }

        var actual = Compute(password, salt, parameters, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private static byte[] Compute(string password, byte[] salt, Argon2Parameters parameters, int length = HashBytes)
    {
        using var argon = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            MemorySize = parameters.MemoryKib,
            Iterations = parameters.Iterations,
            DegreeOfParallelism = parameters.Parallelism,
        };
        return argon.GetBytes(length);
    }

    private static bool TryParse(string? encoded, out Argon2Parameters parameters, out byte[] salt, out byte[] hash)
    {
        parameters = null!;
        salt = hash = [];
        if (encoded is null || !encoded.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var parts = encoded[Prefix.Length..].Split('$');
        if (parts.Length != 3)
        {
            return false;
        }

        int? m = null, t = null, p = null;
        foreach (var pair in parts[0].Split(','))
        {
            var kv = pair.Split('=');
            if (kv.Length != 2 || !int.TryParse(kv[1], NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value < 1)
            {
                return false;
            }

            switch (kv[0])
            {
                case "m": m = value; break;
                case "t": t = value; break;
                case "p": p = value; break;
                default: return false;
            }
        }

        if (m is null || t is null || p is null || !TryDecode(parts[1], out salt) || !TryDecode(parts[2], out hash) || salt.Length < 8 || hash.Length < 16)
        {
            return false;
        }

        parameters = new Argon2Parameters(m.Value, t.Value, p.Value);
        return true;
    }

    // PHC strings use standard base64 without padding.
    private static string Encode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=');

    private static bool TryDecode(string text, out byte[] bytes)
    {
        var padded = text.PadRight(text.Length + ((4 - (text.Length % 4)) % 4), '=');
        bytes = new byte[padded.Length];
        if (Convert.TryFromBase64String(padded, bytes, out var written))
        {
            bytes = bytes[..written];
            return true;
        }

        bytes = [];
        return false;
    }
}
