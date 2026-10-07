using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using MediaMan.Core;
using MediaMan.Core.Domain;
using MediaMan.Data;
using Microsoft.EntityFrameworkCore;

namespace MediaMan.Auth;

/// <summary>The tokens a sign-in or refresh hands to the client.</summary>
public sealed record AuthTokens(
    string AccessToken, DateTimeOffset AccessTokenExpiresAt, string RefreshToken, DateTimeOffset RefreshTokenExpiresAt);

public enum SignInFailure
{
    /// <summary>Unknown user, wrong password or disabled account; deliberately not told apart.</summary>
    InvalidCredentials,

    /// <summary>The refresh token is unknown, expired or reused, its session was revoked, or the user is disabled.</summary>
    SessionExpired,
}

public sealed record SignInResult(AuthTokens? Tokens, SignInFailure? Failure)
{
    public static SignInResult Success(AuthTokens tokens) => new(tokens, null);

    public static SignInResult Failed(SignInFailure failure) => new(null, failure);
}

/// <summary>
/// Sign-in and rotating refresh tokens (SPEC §7.11, ADR 0008). Each sign-in is a <see cref="Session"/> holding only the
/// SHA-256 of its current refresh token. A refresh token is <c>&lt;session id&gt;.&lt;32 random bytes&gt;</c>, so a
/// token that names a live session but no longer matches its hash is a rotated one being reused, and the session is
/// revoked.
/// </summary>
public sealed class SessionService(MediaManDbContext db, PasswordHasher hasher, AccessTokens accessTokens, IClock clock)
{
    public static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(30);
    public const int MaximumDeviceNameLength = 100;
    public const string UnknownDevice = "Unknown device";

    // A hash to verify against when the username is unknown, so the response time doesn't reveal which names exist.
    private static readonly ConcurrentDictionary<Argon2Parameters, string> _dummyHashes = new();

    public async Task<SignInResult> SignInAsync(string username, string password, string? deviceName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(username);
        ArgumentNullException.ThrowIfNull(password);

        var name = username.Trim();
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Username == name, cancellationToken);
        var hash = user?.PasswordHash ?? _dummyHashes.GetOrAdd(hasher.Parameters, _ => hasher.Hash(Guid.NewGuid().ToString()));
        var passwordMatches = PasswordHasher.Verify(password, hash);
        if (user is null || !passwordMatches || user.Disabled)
        {
            return SignInResult.Failed(SignInFailure.InvalidCredentials);
        }

        var now = clock.UtcNow;
        var session = new Session
        {
            Id = Ids.New(clock),
            UserId = user.Id,
            DeviceName = DeviceNameOf(deviceName),
            RefreshTokenHash = "",
            CreatedAt = now,
            LastSeenAt = now,
            ExpiresAt = now + RefreshTokenLifetime,
        };
        var refreshToken = NewRefreshToken(session.Id);
        session.RefreshTokenHash = HashOf(refreshToken);
        db.Sessions.Add(session);
        await db.SaveChangesAsync(cancellationToken);

        return SignInResult.Success(TokensFor(session, refreshToken));
    }

    public async Task<SignInResult> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(refreshToken);

        var expired = SignInResult.Failed(SignInFailure.SessionExpired);
        if (!TryReadSessionId(refreshToken, out var sessionId))
        {
            return expired;
        }

        var session = await db.Sessions.AsNoTracking().SingleOrDefaultAsync(s => s.Id == sessionId, cancellationToken);
        if (session is null || session.Revoked)
        {
            return expired;
        }

        var presentedHash = HashOf(refreshToken);
        if (!HashesEqual(presentedHash, session.RefreshTokenHash))
        {
            await RevokeAsync(session.Id, cancellationToken);
            return expired;
        }

        var now = clock.UtcNow;
        if (session.ExpiresAt <= now)
        {
            return expired;
        }

        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == session.UserId, cancellationToken);
        if (user is null || user.Disabled)
        {
            return expired;
        }

        var next = NewRefreshToken(session.Id);
        var nextHash = HashOf(next);
        var expiresAt = now + RefreshTokenLifetime;

        // Rotate only if the token is still the current one: of two concurrent refreshes with the same token, one wins
        // and the other is treated as reuse.
        var rotated = await db.Sessions
            .Where(s => s.Id == session.Id && s.RefreshTokenHash == presentedHash && !s.Revoked)
            .ExecuteUpdateAsync(u => u
                .SetProperty(s => s.RefreshTokenHash, nextHash)
                .SetProperty(s => s.LastSeenAt, now)
                .SetProperty(s => s.ExpiresAt, expiresAt), cancellationToken);
        if (rotated == 0)
        {
            await RevokeAsync(session.Id, cancellationToken);
            return expired;
        }

        session.ExpiresAt = expiresAt;
        return SignInResult.Success(TokensFor(session, next));
    }

    /// <summary>Ends a session: its refresh token stops working, and enforcement refuses its access tokens.</summary>
    public Task RevokeAsync(Guid sessionId, CancellationToken cancellationToken) =>
        db.Sessions.Where(s => s.Id == sessionId).ExecuteUpdateAsync(u => u.SetProperty(s => s.Revoked, true), cancellationToken);

    private AuthTokens TokensFor(Session session, string refreshToken)
    {
        var (accessToken, accessExpiresAt) = accessTokens.Issue(session.UserId, session.Id);
        return new AuthTokens(accessToken, accessExpiresAt, refreshToken, session.ExpiresAt);
    }

    private static string DeviceNameOf(string? deviceName)
    {
        var name = deviceName?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            return UnknownDevice;
        }

        return name.Length > MaximumDeviceNameLength ? name[..MaximumDeviceNameLength] : name;
    }

    private static string NewRefreshToken(Guid sessionId) =>
        sessionId.ToString("N") + "." + Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    private static bool TryReadSessionId(string refreshToken, out Guid sessionId)
    {
        sessionId = Guid.Empty;
        var dot = refreshToken.IndexOf('.', StringComparison.Ordinal);
        return dot > 0 && Guid.TryParseExact(refreshToken.AsSpan(0, dot), "N", out sessionId);
    }

    // The token is 32 random bytes, so a fast hash is enough: there's nothing to brute-force.
    private static string HashOf(string refreshToken) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken)));

    private static bool HashesEqual(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(a), Encoding.ASCII.GetBytes(b));
}
