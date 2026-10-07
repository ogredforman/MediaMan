using System.Security.Cryptography;
using System.Text;

namespace MediaMan.Auth;

/// <summary>
/// The per-install key (SPEC §9, ADR 0003, ADR 0008): 32 random bytes generated on first run and stored next to the
/// database. Nothing uses it directly; each use gets its own subkey from <see cref="Derive"/>, so a token signature
/// can never be replayed as a ciphertext or the other way round.
/// </summary>
public sealed class InstallKey
{
    public const int SizeBytes = 32;

    /// <summary>Subkey for encrypting secrets at rest with <c>AesGcmSecretProtector</c>.</summary>
    public const string SecretsAtRest = "secrets-at-rest";

    private readonly byte[] _key;

    private InstallKey(byte[] key) => _key = key;

    public static InstallKey FromBytes(ReadOnlySpan<byte> key)
    {
        if (key.Length != SizeBytes)
        {
            throw new ArgumentException($"The install key must be {SizeBytes} bytes.", nameof(key));
        }

        return new InstallKey(key.ToArray());
    }

    /// <summary>
    /// Reads the key file at <paramref name="path"/>, creating it first if it doesn't exist. Synchronous because it
    /// runs once, from a singleton factory at startup, on a 32-byte file.
    /// </summary>
    /// <exception cref="InvalidOperationException">The file exists but isn't a valid key. It's never replaced, since
    /// a new key would make every stored secret unreadable.</exception>
    public static InstallKey LoadOrCreate(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            Create(path);
        }

        var bytes = File.ReadAllBytes(path);
        if (bytes.Length != SizeBytes)
        {
            throw new InvalidOperationException(
                $"The install key file '{path}' is damaged: it should be {SizeBytes} bytes but is {bytes.Length}. " +
                "Restore it from the backup you took with the database. If there is none, delete it and restart; " +
                "everyone then signs in again and stored secrets (such as VPN proxy passwords) must be re-entered.");
        }

        return new InstallKey(bytes);
    }

    /// <summary>A 32-byte subkey for <paramref name="purpose"/> (HKDF-SHA256).</summary>
    public byte[] Derive(string purpose)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
        return HKDF.DeriveKey(HashAlgorithmName.SHA256, _key, SizeBytes, salt: [], info: Encoding.UTF8.GetBytes("MediaMan/" + purpose));
    }

    /// <summary>Writes a new key to a temp file and moves it into place, so a crash never leaves a partial key.</summary>
    private static void Create(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var key = RandomNumberGenerator.GetBytes(SizeBytes);
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Options = FileOptions.WriteThrough };
            if (!OperatingSystem.IsWindows())
            {
                // Owner-only on Unix. On Windows the data directory's per-user ACL already restricts it.
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            }

            using (var stream = new FileStream(temp, options))
            {
                stream.Write(key);
            }

            File.Move(temp, path, overwrite: false);
        }
        catch (IOException) when (File.Exists(path))
        {
            // Another process created the key first; use theirs.
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            File.Delete(temp);
        }
    }
}
