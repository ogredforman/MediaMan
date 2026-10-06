using MediaMan.Data;

namespace MediaMan.Server.Hosting;

/// <summary>
/// Stands in until PLAN 2.3 generates the per-install key and registers <see cref="AesGcmSecretProtector"/>.
/// Nothing stores a secret before then; if something tries, it fails loudly instead of storing it unprotected.
/// </summary>
public sealed class UnavailableSecretProtector : ISecretProtector
{
    public string Protect(string plaintext) => throw Unavailable();

    public string Unprotect(string protectedText) => throw Unavailable();

    private static InvalidOperationException Unavailable() =>
        new("Secrets can't be stored yet: the per-install key arrives with accounts (PLAN 2.3).");
}
