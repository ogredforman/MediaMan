namespace MediaMan.Data;

/// <summary>
/// Encrypts secrets stored at rest (TOTP secrets, SOCKS5 credentials) with the per-install key
/// (SPEC §9, ADR 0003). Register one instance per process: the EF model is cached per protector.
/// </summary>
public interface ISecretProtector
{
    string Protect(string plaintext);

    /// <exception cref="System.Security.Cryptography.CryptographicException">The value was tampered with or protected with another key.</exception>
    string Unprotect(string protectedText);
}
