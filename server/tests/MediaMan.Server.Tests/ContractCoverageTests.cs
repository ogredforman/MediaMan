using System.Text.Json.Nodes;
using MediaMan.Core;
using MediaMan.Server.Endpoints;
using MediaMan.Server.Tests.Conformance;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace MediaMan.Server.Tests;

/// <summary>
/// Contracts first (SPEC §5): the server maps exactly the contract's operations, and every route declares the
/// permission the contract gives it. PLAN 2.3 enforces these permissions with the table-driven auth test.
/// </summary>
public sealed class ContractCoverageTests
{
    [Fact]
    public async Task Every_mapped_route_is_in_the_contract_with_the_same_permission()
    {
        var routes = await MappedRoutesAsync();

        routes.Should().NotBeEmpty();
        foreach (var route in routes)
        {
            var operation = ContractDocument.Instance.Operations
                .SingleOrDefault(o => o.Method == route.Method && o.Path == route.Path);
            operation.Should().NotBeNull($"{route.Method} {route.Path} is mapped, so the contract must declare it");
            route.Permission.Should().NotBeNull($"{route.Method} {route.Path} must declare its permission");
            route.Permission.Should().Be(operation!.Permission, $"{route.Method} {route.Path} must match the contract's x-permission");
            route.SignedUrl.Should().Be(operation.AcceptsSignedUrl, $"{route.Method} {route.Path} must accept signed URLs exactly when the contract lists signedUrl");
        }
    }

    [Fact]
    public async Task Every_contract_operation_is_mapped()
    {
        var routes = await MappedRoutesAsync();

        foreach (var operation in ContractDocument.Instance.Operations.Where(o => !o.DevOnly))
        {
            routes.Should().Contain(r => r.Method == operation.Method && r.Path == operation.Path, $"{operation} is in the contract");
        }
    }

    [Fact]
    public void Every_operation_declares_a_known_permission_and_matching_security()
    {
        foreach (var operation in ContractDocument.Instance.Operations)
        {
            var permission = operation.Permission;
            var known = permission is PermissionMetadata.None or PermissionMetadata.Authenticated || Permissions.IsKnown(permission);
            known.Should().BeTrue($"{operation} declares x-permission '{permission}'");

            var security = operation.Node["security"] as JsonArray;
            var unauthenticated = security is { Count: 0 };
            unauthenticated.Should().Be(permission == PermissionMetadata.None,
                $"{operation}: only x-permission 'none' may override security with []");
        }
    }

    [Fact]
    public void Every_operation_uses_the_shared_error_model()
    {
        foreach (var operation in ContractDocument.Instance.Operations)
        {
            operation.Node["responses"]?["default"]?["$ref"]?.GetValue<string>()
                .Should().Be("#/components/responses/Problem", $"{operation} must use the shared error model");
        }
    }

    [Fact]
    public async Task Dev_only_operations_are_not_mapped_outside_the_dev_configuration()
    {
        var routes = await MappedRoutesAsync();

        foreach (var operation in ContractDocument.Instance.Operations.Where(o => o.DevOnly))
        {
            routes.Should().NotContain(r => r.Method == operation.Method && r.Path == operation.Path, $"{operation} is dev-only (SPEC §13)");
        }
    }

    internal static async Task<List<(string Method, string Path, string? Permission, bool SignedUrl)>> MappedRoutesAsync(MediaManFactory? factory = null)
    {
        await using var owned = factory is null ? new MediaManFactory() : null;
        factory ??= owned!;
        _ = factory.Server;
        var source = factory.Services.GetRequiredService<EndpointDataSource>();

        return [.. source.Endpoints
            .OfType<RouteEndpoint>()
            .SelectMany(e => (e.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? []).Select(method => (
                Method: method,
                Path: "/" + e.RoutePattern.RawText!.TrimStart('/'),
                e.Metadata.GetMetadata<PermissionMetadata>()?.Permission,
                SignedUrl: e.Metadata.GetMetadata<SignedUrlMetadata>() is not null)))];
    }
}
