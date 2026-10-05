using System.Security.Cryptography;
using System.Text;

namespace MediaMan.Data;

/// <summary>
/// AES-256-GCM with a random nonce per value. Stored form: <c>v1:</c> + base64(nonce | tag | ciphertext).
/// </summary>
public sealed class AesGcmSecretProtector : ISecretProtector
{
    public const int KeySizeBytes = 32;

    private const string Prefix = "v1:";
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly byte[] _key;

    public AesGcmSecretProtector(ReadOnlySpan<byte> key)
    {
        if (key.Length != KeySizeBytes)
        {
            throw new ArgumentException($"The secret key must be {KeySizeBytes} bytes.", nameof(key));
        }

        _key = key.ToArray();
    }

    public string Protect(string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);

        var plain = Encoding.UTF8.GetBytes(plaintext);
        var buffer = new byte[NonceSize + TagSize + plain.Length];
        var nonce = buffer.AsSpan(0, NonceSize);
        RandomNumberGenerator.Fill(nonce);

        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plain, buffer.AsSpan(NonceSize + TagSize), buffer.AsSpan(NonceSize, TagSize));
        return Prefix + Convert.ToBase64String(buffer);
    }

    public string Unprotect(string protectedText)
    {
        ArgumentNullException.ThrowIfNull(protectedText);

        if (!protectedText.StartsWith(Prefix, StringComparison.Ordinal))
        {
            throw new CryptographicException("The stored secret is not in a recognised protected format.");
        }

        byte[] buffer;
        try
        {
            buffer = Convert.FromBase64String(protectedText[Prefix.Length..]);
        }
        catch (FormatException ex)
        {
            throw new CryptographicException("The stored secret is not in a recognised protected format.", ex);
        }

        if (buffer.Length < NonceSize + TagSize)
        {
            throw new CryptographicException("The stored secret is truncated.");
        }

        var plain = new byte[buffer.Length - NonceSize - TagSize];
        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(
            buffer.AsSpan(0, NonceSize),
            buffer.AsSpan(NonceSize + TagSize),
            buffer.AsSpan(NonceSize, TagSize),
            plain);
        return Encoding.UTF8.GetString(plain);
    }
}
