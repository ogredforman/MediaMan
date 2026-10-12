using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MediaMan.Auth;
using MediaMan.Core;
using MediaMan.Server.Tests.Conformance;

namespace MediaMan.Server.Tests;

/// <summary>The signed-in account at <c>GET /account</c> (SPEC §7.11, §11): what clients hide sections by.</summary>
public sealed class AccountApiTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_superadmin_holds_every_permission()
    {
        await using var factory = new MediaManFactory();
        using var client = factory.CreateClient();
        using var login = await client.PostAsJsonAsync(new Uri("/auth/login", UriKind.Relative),
            new { username = MediaManFactory.SuperadminUsername, password = MediaManFactory.SuperadminPassword }, Ct);
        var tokens = (await login.Content.ReadFromJsonAsync<AuthTokens>(_json, Ct))!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

        using var response = await client.GetAsync(new Uri("/account", UriKind.Relative), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await ContractDocument.Instance.AssertConformsAsync(response, Ct);
        var account = (await response.Content.ReadFromJsonAsync<Account>(_json, Ct))!;
        account.Username.Should().Be(MediaManFactory.SuperadminUsername);
        account.IsSuperadmin.Should().BeTrue();
        account.Permissions.Should().BeEquivalentTo(Permissions.All);
    }

    [Fact]
    public async Task A_user_sees_only_the_permissions_of_their_role()
    {
        await using var factory = new MediaManFactory();
        var user = await factory.CreateUserAsync(Ct, Permissions.LibraryView, Permissions.SettingsManage);
        using var client = factory.CreateClient(user);

        using var response = await client.GetAsync(new Uri("/account", UriKind.Relative), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await ContractDocument.Instance.AssertConformsAsync(response, Ct);
        var account = (await response.Content.ReadFromJsonAsync<Account>(_json, Ct))!;
        account.Id.Should().Be(user.UserId);
        account.IsSuperadmin.Should().BeFalse();
        account.Permissions.Should().Equal(Permissions.LibraryView, Permissions.SettingsManage);
    }

    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    private sealed record Account(Guid Id, string Username, bool IsSuperadmin, string[] Permissions);
}
