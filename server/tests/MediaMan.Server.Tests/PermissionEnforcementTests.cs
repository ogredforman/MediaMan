using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MediaMan.Auth;
using MediaMan.Data;
using MediaMan.Fakes;
using MediaMan.Server.Endpoints;
using MediaMan.Server.Tests.Conformance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MediaMan.Server.Tests;

/// <summary>
/// The table-driven permission test (SPEC §5, §9, §12): every mapped route, including the dev-only ones, enforces the
/// permission it declares. Unauthenticated routes stay open; every other route refuses a missing, forged or expired
/// token, a revoked session and a disabled user with 401, a user without the permission with 403, and lets a user
/// holding exactly that permission through.
/// </summary>
public sealed partial class PermissionEnforcementTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mediaman-tests", Guid.NewGuid().ToString("N"));

    public PermissionEnforcementTests() => Directory.CreateDirectory(Path.Combine(_root, "Media"));

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Every_route_enforces_its_permission()
    {
        await using var factory = new MediaManFactory();

        await AssertEveryRouteAsync(factory);
    }

    [Fact]
    public async Task Every_route_enforces_its_permission_in_the_dev_configuration()
    {
        // Auto sign-in is off, so the dev configuration enforces permissions like any other.
        await using var factory = new MediaManFactory("Development", new Dictionary<string, string>
        {
            ["MediaMan:Dev:MediaPath"] = Path.Combine(_root, "Media"),
            ["MediaMan:Dev:Providers:Player"] = "Fake",
        });

        await AssertEveryRouteAsync(factory);
    }

    private static async Task AssertEveryRouteAsync(MediaManFactory factory)
    {
        var routes = await ContractCoverageTests.MappedRoutesAsync(factory);
        routes.Should().Contain(r => r.Permission == PermissionMetadata.None)
            .And.Contain(r => r.Permission == PermissionMetadata.Authenticated)
            .And.Contain(r => r.Permission != PermissionMetadata.None && r.Permission != PermissionMetadata.Authenticated);

        var outsider = await factory.CreateUserAsync(Ct);
        var revoked = await factory.CreateUserAsync(Ct, [.. MediaMan.Core.Permissions.All]);
        var disabled = await factory.CreateUserAsync(Ct, [.. MediaMan.Core.Permissions.All]);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<SessionService>().RevokeAsync(revoked.SessionId, Ct);
            var db = scope.ServiceProvider.GetRequiredService<MediaManDbContext>();
            var user = await db.Users.SingleAsync(u => u.Id == disabled.UserId, Ct);
            user.Disabled = true;
            await db.SaveChangesAsync(Ct);
        }

        var expired = ExpiredToken(factory, outsider);
        var forged = ForgedToken(outsider);
        var holders = new Dictionary<string, TestUser>();

        routes.Should().Contain(r => r.SignedUrl).And.Contain(r => r.SessionUrl);
        var signer = factory.Services.GetRequiredService<UrlSigner>();
        var installKey = factory.Services.GetRequiredService<InstallKey>().Derive(InstallKey.ArtworkUrls);
        var expiredSigner = new UrlSigner(installKey, new FakeClock(DateTimeOffset.UtcNow - TimeSpan.FromDays(3)));
        var foreignSigner = new UrlSigner(new byte[InstallKey.SizeBytes], new FakeClock(DateTimeOffset.UtcNow));

        foreach (var (method, template, permission, signedUrl, sessionUrl) in routes)
        {
            var path = RouteParameter().Replace(template, _ => Guid.NewGuid().ToString());
            var route = $"{method} {template}";

            if (signedUrl)
            {
                // A signed URL stands in for a token; any other signature counts as no token at all.
                var signed = signer.Sign(path);
                var otherPath = RouteParameter().Replace(template, _ => Guid.NewGuid().ToString());
                using (var allowed = await SendAsync(factory, method, signed, token: null))
                {
                    allowed.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized, $"{route} with a signed URL");
                    allowed.StatusCode.Should().NotBe(HttpStatusCode.Forbidden, $"{route} with a signed URL");
                }

                await AssertProblemAsync(factory, method, otherPath + signed[path.Length..], null, HttpStatusCode.Unauthorized, "auth.required", $"{route} signed for another path");
                await AssertProblemAsync(factory, method, signed[..^1] + (signed[^1] == 'A' ? 'B' : 'A'), null, HttpStatusCode.Unauthorized, "auth.required", $"{route} with a tampered signature");
                await AssertProblemAsync(factory, method, expiredSigner.Sign(path), null, HttpStatusCode.Unauthorized, "auth.required", $"{route} with an expired signature");
                await AssertProblemAsync(factory, method, foreignSigner.Sign(path), null, HttpStatusCode.Unauthorized, "auth.required", $"{route} signed with another key");
            }

            if (sessionUrl)
            {
                // A session-signed URL counts as that session's token: live session, enabled user, and the permission.
                if (!holders.TryGetValue(permission!, out var sessionHolder))
                {
                    sessionHolder = holders[permission!] = await factory.CreateUserAsync(Ct, permission!);
                }

                var signed = signer.SignForSession(path, sessionHolder.SessionId);
                var otherPath = RouteParameter().Replace(template, _ => Guid.NewGuid().ToString());
                await AssertAllowedAsync(factory, method, signed, token: null, $"{route} with a session-signed URL");
                await AssertProblemAsync(factory, method, signer.Sign(path), null, HttpStatusCode.Unauthorized, "auth.required", $"{route} with an unbound signature");
                await AssertProblemAsync(factory, method, otherPath + signed[path.Length..], null, HttpStatusCode.Unauthorized, "auth.required", $"{route} session-signed for another path");
                await AssertProblemAsync(factory, method, signed[..^1] + (signed[^1] == 'A' ? 'B' : 'A'), null, HttpStatusCode.Unauthorized, "auth.required", $"{route} with a tampered session signature");
                await AssertProblemAsync(factory, method, expiredSigner.SignForSession(path, sessionHolder.SessionId), null, HttpStatusCode.Unauthorized, "auth.required", $"{route} with an expired session signature");
                await AssertProblemAsync(factory, method, foreignSigner.SignForSession(path, sessionHolder.SessionId), null, HttpStatusCode.Unauthorized, "auth.required", $"{route} session-signed with another key");
                await AssertProblemAsync(factory, method, signer.SignForSession(path, revoked.SessionId), null, HttpStatusCode.Unauthorized, "auth.session_expired", $"{route} session-signed for a revoked session");
                await AssertProblemAsync(factory, method, signer.SignForSession(path, disabled.SessionId), null, HttpStatusCode.Unauthorized, "auth.session_expired", $"{route} session-signed for a disabled user");
                await AssertProblemAsync(factory, method, signer.SignForSession(path, Guid.CreateVersion7()), null, HttpStatusCode.Unauthorized, "auth.session_expired", $"{route} session-signed for an unknown session");
                await AssertProblemAsync(factory, method, signer.SignForSession(path, outsider.SessionId), null, HttpStatusCode.Forbidden, "auth.forbidden", $"{route} session-signed for a user without '{permission}'");
            }

            if (permission == PermissionMetadata.None)
            {
                using var open = await SendAsync(factory, method, path, token: null);
                open.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized, $"{route} is unauthenticated");
                open.StatusCode.Should().NotBe(HttpStatusCode.Forbidden, $"{route} is unauthenticated");
                continue;
            }

            await AssertProblemAsync(factory, method, path, null, HttpStatusCode.Unauthorized, "auth.required", $"{route} without a token");
            await AssertProblemAsync(factory, method, path, "not-a-token", HttpStatusCode.Unauthorized, "auth.required", $"{route} with a malformed token");
            await AssertProblemAsync(factory, method, path, forged, HttpStatusCode.Unauthorized, "auth.required", $"{route} with a forged token");
            await AssertProblemAsync(factory, method, path, expired, HttpStatusCode.Unauthorized, "auth.required", $"{route} with an expired token");
            await AssertProblemAsync(factory, method, path, revoked.AccessToken, HttpStatusCode.Unauthorized, "auth.session_expired", $"{route} with a revoked session");
            await AssertProblemAsync(factory, method, path, disabled.AccessToken, HttpStatusCode.Unauthorized, "auth.session_expired", $"{route} as a disabled user");

            if (permission == PermissionMetadata.Authenticated)
            {
                // A fresh user each time: one of these routes is sign-out, which ends the session it's called with.
                var anyone = await factory.CreateUserAsync(Ct);
                await AssertAllowedAsync(factory, method, path, anyone.AccessToken, $"{route} as any signed-in user");
                continue;
            }

            await AssertProblemAsync(factory, method, path, outsider.AccessToken, HttpStatusCode.Forbidden, "auth.forbidden", $"{route} without '{permission}'");
            if (!holders.TryGetValue(permission!, out var holder))
            {
                holder = holders[permission!] = await factory.CreateUserAsync(Ct, permission!);
            }

            await AssertAllowedAsync(factory, method, path, holder.AccessToken, $"{route} with only '{permission}'");
        }
    }

    private static async Task AssertProblemAsync(
        MediaManFactory factory, string method, string path, string? token, HttpStatusCode status, string code, string because)
    {
        using var response = await SendAsync(factory, method, path, token);

        response.StatusCode.Should().Be(status, because);
        await ContractDocument.Instance.AssertConformsAsync(response, Ct);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        body.RootElement.GetProperty("code").GetString().Should().Be(code, because);
        body.RootElement.GetProperty("action").GetString().Should().NotBeNullOrWhiteSpace(because);
        if (status == HttpStatusCode.Unauthorized)
        {
            response.Headers.WwwAuthenticate.Should().ContainSingle(h => h.Scheme == "Bearer", because);
        }
    }

    private static async Task AssertAllowedAsync(MediaManFactory factory, string method, string path, string? token, string because)
    {
        using var response = await SendAsync(factory, method, path, token);

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized, because);
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden, because);
    }

    /// <summary>Sends an empty JSON body to anything but GET; reads only the headers, since <c>/events</c> never ends.</summary>
    private static async Task<HttpResponseMessage> SendAsync(MediaManFactory factory, string method, string path, string? token)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), new Uri(path, UriKind.Relative));
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (method != "GET")
        {
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        }

        return await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, Ct);
    }

    /// <summary>A token the server signed for a live session, but long expired.</summary>
    private static string ExpiredToken(MediaManFactory factory, TestUser user)
    {
        var key = factory.Services.GetRequiredService<InstallKey>().Derive(InstallKey.AccessTokens);
        var clock = new FakeClock(DateTimeOffset.UtcNow - TimeSpan.FromDays(1));
        return new AccessTokens(key, clock).Issue(user.UserId, user.SessionId).Token;
    }

    /// <summary>A well-formed token for a live session, signed with a key other than this install's.</summary>
    private static string ForgedToken(TestUser user)
    {
        var key = InstallKey.FromBytes(new byte[InstallKey.SizeBytes]).Derive(InstallKey.AccessTokens);
        return new AccessTokens(key, new FakeClock(DateTimeOffset.UtcNow)).Issue(user.UserId, user.SessionId).Token;
    }

    [GeneratedRegex(@"\{[^}]+\}")]
    private static partial Regex RouteParameter();
}
