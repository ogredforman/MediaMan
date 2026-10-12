using System.Security.Cryptography;
using System.Text.RegularExpressions;
using MediaMan.Core.Domain;
using MediaMan.Data;
using MediaMan.Server.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MediaMan.Server.Settings;

public sealed record MetadataSettingsRequest(string? TmdbAccessToken);

public sealed record MetadataSettings(string TmdbAccessTokenSource);

/// <summary>
/// The TMDB token set in Settings → Metadata, held in memory for <c>TmdbOptions.AccessTokenOverride</c> so a change
/// applies from the next request. A secret: never logged (SPEC §9).
/// </summary>
public sealed class TmdbTokenOverride
{
    private volatile string? _token;

    public string? Token
    {
        get => _token;
        set => _token = value;
    }
}

/// <summary>
/// Settings → Metadata (SPEC §8 metadata [M], §11): the TMDB token override, stored encrypted with the per-install key
/// in the <c>metadata.tmdb.accessToken</c> Setting (SPEC §9 secrets at rest).
/// </summary>
public sealed partial class MetadataSettingsService(
    MediaManDbContext db,
    ISecretProtector protector,
    TmdbTokenOverride tokenOverride,
    IOptions<MediaManOptions> options)
{
    public const string TmdbTokenKey = "metadata.tmdb.accessToken";

    /// <summary>Tokens are printable base64url-ish text (a JWT for TMDB's read-access token); anything else isn't one.</summary>
    [GeneratedRegex("^[A-Za-z0-9._~+/=-]+$")]
    private static partial Regex TokenCharacters();

    /// <summary>The v3 API key is 32 hex characters; TMDB only accepts the read-access token as a bearer token.</summary>
    [GeneratedRegex("^[0-9a-fA-F]{32}$")]
    private static partial Regex ApiKey();

    public MetadataSettings Get() => new(
        tokenOverride.Token is not null ? "settings"
        : string.IsNullOrWhiteSpace(options.Value.Metadata.Tmdb.AccessToken) ? "shipped"
        : "configuration");

    /// <summary>Why the value can't be a read-access token, or null if it can.</summary>
    public static string? Validate(string? token)
    {
        var value = token?.Trim() ?? "";
        if (value.Length == 0)
        {
            return null;
        }

        if (value.Length > 1000 || !TokenCharacters().IsMatch(value))
        {
            return "That isn't a TMDB read-access token. Copy the API Read Access Token from your TMDB account's API page.";
        }

        return ApiKey().IsMatch(value)
            ? "That's the short API key. Paste the longer API Read Access Token from the same TMDB page instead."
            : null;
    }

    /// <summary>Saves a valid token (see <see cref="Validate"/>), or clears the override when it's blank.</summary>
    public async Task<MetadataSettings> SetAsync(string? token, CancellationToken cancellationToken)
    {
        var value = token?.Trim() ?? "";
        var setting = await db.Settings.SingleOrDefaultAsync(s => s.Key == TmdbTokenKey, cancellationToken);
        if (value.Length == 0)
        {
            if (setting is not null)
            {
                db.Settings.Remove(setting);
            }
        }
        else if (setting is null)
        {
            db.Settings.Add(new Setting { Key = TmdbTokenKey, Value = protector.Protect(value) });
        }
        else
        {
            setting.Value = protector.Protect(value);
        }

        await db.SaveChangesAsync(cancellationToken);
        tokenOverride.Token = value.Length == 0 ? null : value;
        return Get();
    }

    /// <summary>
    /// Loads the stored override into memory. Returns false if it can't be decrypted (e.g. the key file was replaced);
    /// the configured or shipped token is used then.
    /// </summary>
    public async Task<bool> LoadAsync(CancellationToken cancellationToken)
    {
        var stored = await db.Settings.AsNoTracking()
            .Where(s => s.Key == TmdbTokenKey).Select(s => s.Value).SingleOrDefaultAsync(cancellationToken);
        if (stored is null)
        {
            tokenOverride.Token = null;
            return true;
        }

        try
        {
            tokenOverride.Token = protector.Unprotect(stored);
            return true;
        }
        catch (CryptographicException)
        {
            tokenOverride.Token = null;
            return false;
        }
    }
}

/// <summary>Loads the TMDB token override after the schema exists and before scanning starts.</summary>
public sealed partial class MetadataSettingsLoader(IServiceScopeFactory scopes, ILogger<MetadataSettingsLoader> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        if (!await scope.ServiceProvider.GetRequiredService<MetadataSettingsService>().LoadAsync(cancellationToken))
        {
            LogUnreadableToken();
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "The TMDB access token saved in Settings couldn't be decrypted with this install's key; using the default token. Set it again in Settings → Metadata")]
    private partial void LogUnreadableToken();
}
