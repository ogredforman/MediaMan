namespace MediaMan.Server.Endpoints;

/// <summary>
/// The permission an endpoint requires; mirrors the operation's <c>x-permission</c> in the contract.
/// <see cref="PermissionEnforcement"/> enforces it, and tests check it against the contract.
/// </summary>
public sealed record PermissionMetadata(string Permission)
{
    /// <summary>Unauthenticated: setup, login, invite redemption and health only (SPEC §9).</summary>
    public const string None = "none";

    /// <summary>Any signed-in user.</summary>
    public const string Authenticated = "authenticated";

    /// <summary>The Superadmin only, whatever permissions a role grants (SPEC §7.11).</summary>
    public const string Superadmin = "superadmin";
}

public static class PermissionMetadataExtensions
{
    public static TBuilder RequiresPermission<TBuilder>(this TBuilder builder, string permission)
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new PermissionMetadata(permission));

    /// <summary>
    /// Lets a session that must set a password (SPEC §7.11) through; every other authenticated route refuses it with
    /// <c>403 auth.password_change_required</c>.
    /// </summary>
    public static TBuilder AllowedDuringPasswordChange<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new AllowedDuringPasswordChangeMetadata());

    /// <summary>Accepts a valid, unexpired <c>?exp=…&amp;sig=…</c> from <c>UrlSigner</c> in place of a bearer token.</summary>
    public static TBuilder AllowsSignedUrl<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new SignedUrlMetadata());

    /// <summary>
    /// Accepts a valid, unexpired <c>?sid=…&amp;exp=…&amp;sig=…</c> from <c>UrlSigner.SignForSession</c> in place of that
    /// session's bearer token: the session must still be live and its user must hold the route's permission.
    /// </summary>
    public static TBuilder AllowsSessionSignedUrl<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new SignedUrlMetadata(BoundToSession: true));
}

/// <summary>
/// The endpoint also accepts a URL signed by <c>UrlSigner</c> in place of a bearer token; mirrors the contract's
/// <c>signedUrl</c> security scheme. The signature isn't bound to a user, so it's issued only in responses to callers
/// who already hold the route's permission. With <paramref name="BoundToSession"/> it mirrors <c>sessionUrl</c>
/// instead: the URL is signed for one session and counts as that session's token.
/// </summary>
public sealed record SignedUrlMetadata(bool BoundToSession = false);

/// <summary>See <see cref="PermissionMetadataExtensions.AllowedDuringPasswordChange"/>.</summary>
public sealed record AllowedDuringPasswordChangeMetadata;
