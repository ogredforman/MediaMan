using MediaMan.Core;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace MediaMan.Auth;

/// <summary>The claims of a valid access token.</summary>
public sealed record AccessTokenClaims(Guid UserId, Guid SessionId);

/// <summary>
/// Short-lived JWT access tokens (SPEC §7.11, §9): HS256 on the install key's <see cref="InstallKey.AccessTokens"/>
/// subkey, carrying the user (<c>sub</c>) and the session (<c>sid</c>), so a revoked session can be refused while its
/// token is still unexpired (ADR 0008). Every time comes from <see cref="IClock"/>.
/// </summary>
public sealed class AccessTokens
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    public const string Issuer = "mediaman";
    public const string Audience = "mediaman";
    public const string SessionIdClaim = "sid";

    private readonly SymmetricSecurityKey _key;
    private readonly IClock _clock;
    private readonly JsonWebTokenHandler _handler = new() { SetDefaultTimesOnTokenCreation = false };

    /// <param name="signingKey">The derived subkey; copied, so the caller may zero its array.</param>
    public AccessTokens(ReadOnlySpan<byte> signingKey, IClock clock)
    {
        if (signingKey.Length < InstallKey.SizeBytes)
        {
            throw new ArgumentException($"The signing key must be at least {InstallKey.SizeBytes} bytes.", nameof(signingKey));
        }

        _key = new SymmetricSecurityKey(signingKey.ToArray());
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        ValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = Issuer,
            ValidAudience = Audience,
            IssuerSigningKey = _key,
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            RequireSignedTokens = true,
            RequireExpirationTime = true,
            ValidateLifetime = true,
            LifetimeValidator = IsCurrent,
        };
    }

    /// <summary>How tokens are validated; JwtBearer uses the same parameters.</summary>
    public TokenValidationParameters ValidationParameters { get; }

    /// <summary>A token for <paramref name="userId"/> in <paramref name="sessionId"/>, expiring <see cref="Lifetime"/> from now.</summary>
    public (string Token, DateTimeOffset ExpiresAt) Issue(Guid userId, Guid sessionId)
    {
        // JWT times are whole seconds; report the expiry the token actually carries.
        var now = DateTimeOffset.FromUnixTimeSeconds(_clock.UtcNow.ToUnixTimeSeconds());
        var expiresAt = now + Lifetime;
        var token = _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Audience,
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Sub] = userId.ToString(),
                [SessionIdClaim] = sessionId.ToString(),
            },
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = new SigningCredentials(_key, SecurityAlgorithms.HmacSha256),
        });
        return (token, expiresAt);
    }

    /// <summary>The claims of <paramref name="token"/>, or null when it's malformed, forged, expired or not yet valid.</summary>
    public async Task<AccessTokenClaims?> ValidateAsync(string token)
    {
        ArgumentNullException.ThrowIfNull(token);

        var result = await _handler.ValidateTokenAsync(token, ValidationParameters);
        if (!result.IsValid || result.SecurityToken is not JsonWebToken jwt)
        {
            return null;
        }

        return Guid.TryParse(jwt.Subject, out var userId)
            && jwt.TryGetPayloadValue<string>(SessionIdClaim, out var sid)
            && Guid.TryParse(sid, out var sessionId)
                ? new AccessTokenClaims(userId, sessionId)
                : null;
    }

    private bool IsCurrent(DateTime? notBefore, DateTime? expires, SecurityToken token, TokenValidationParameters parameters)
    {
        var now = _clock.UtcNow.UtcDateTime;
        return expires is { } end && now < end && (notBefore is not { } start || start <= now);
    }
}
