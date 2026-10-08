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
}

public static class PermissionMetadataExtensions
{
    public static TBuilder RequiresPermission<TBuilder>(this TBuilder builder, string permission)
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new PermissionMetadata(permission));

    /// <summary>Accepts a valid, unexpired <c>?exp=…&amp;sig=…</c> from <c>UrlSigner</c> in place of a bearer token.</summary>
    public static TBuilder AllowsSignedUrl<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new SignedUrlMetadata());
}

/// <summary>
/// The endpoint also accepts a URL signed by <c>UrlSigner</c> in place of a bearer token; mirrors the contract's
/// <c>signedUrl</c> security scheme. The signature isn't bound to a user, so it's issued only in responses to callers
/// who already hold the route's permission.
/// </summary>
public sealed record SignedUrlMetadata;
