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
}
