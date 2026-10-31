using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MediaMan.Data;
using MediaMan.Server.Tests.Conformance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MediaMan.Server.Tests;

/// <summary>
/// The password policy (SPEC §7.11): setup's choice, <c>/settings/password-policy</c>, sign-in under each policy, the
/// forced password change, and that the Superadmin can never end up without a way in (SPEC §5).
/// </summary>
public sealed class PasswordPolicyApiTests : IDisposable
{
    private const string Strong = "Correct-Horse-9";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "mediaman-tests", Guid.NewGuid().ToString("N"));

    public PasswordPolicyApiTests() => Directory.CreateDirectory(Path.Combine(_root, "Movies"));

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_default_policy_requires_passwords_of_8_characters()
    {
        await using var factory = new MediaManFactory();
        using var owner = await SignedInAsync(factory, MediaManFactory.SuperadminUsername, MediaManFactory.SuperadminPassword);

        (await PolicyAsync(owner)).Should().Be((true, false));
        using var options = await factory.CreateClient().GetAsync(new Uri("/auth/sign-in-options", UriKind.Relative), Ct);
        await ContractDocument.Instance.AssertConformsAsync(options, Ct);
        using var body = await JsonOfAsync(options);
        body.RootElement.GetProperty("passwordsRequired").GetBoolean().Should().BeTrue();
        body.RootElement.TryGetProperty("accounts", out _).Should().BeFalse("accounts are listed only while passwords are off");
        body.RootElement.GetProperty("strongPasswords").GetBoolean().Should().BeFalse();

        (await SetPasswordAsync(owner, MediaManFactory.SuperadminPassword, "seven77")).Should().Be("newPassword");
        (await SetPasswordAsync(owner, MediaManFactory.SuperadminPassword, "eight888")).Should().BeNull();
    }

    [Fact]
    public async Task Only_the_Superadmin_changes_the_policy()
    {
        await using var factory = new MediaManFactory();
        using var admin = factory.CreateClient(await factory.CreateUserAsync(Ct, [.. MediaMan.Core.Permissions.All]));
        using var owner = await SignedInAsync(factory, MediaManFactory.SuperadminUsername, MediaManFactory.SuperadminPassword);

        using var refused = await admin.PutAsJsonAsync("/settings/password-policy", new { requirePasswords = false, strongPasswords = false }, Ct);
        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await ContractDocument.Instance.AssertConformsAsync(refused, Ct);

        using var incomplete = await owner.PutAsJsonAsync("/settings/password-policy", new { strongPasswords = true }, Ct);
        incomplete.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await ContractDocument.Instance.AssertConformsAsync(incomplete, Ct);

        await SetPolicyAsync(owner, require: true, strong: true);
        (await PolicyAsync(owner)).Should().Be((true, true));

        // A session that must set a new password can't read the policy, so the sign-in options give the rule too.
        using var options = await factory.CreateClient().GetAsync(new Uri("/auth/sign-in-options", UriKind.Relative), Ct);
        await ContractDocument.Instance.AssertConformsAsync(options, Ct);
        using var body = await JsonOfAsync(options);
        body.RootElement.GetProperty("strongPasswords").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task A_tightened_policy_makes_the_Superadmin_set_a_new_password_and_then_lets_them_in()
    {
        await using var factory = new MediaManFactory();
        using (var owner = await SignedInAsync(factory, MediaManFactory.SuperadminUsername, MediaManFactory.SuperadminPassword))
        {
            await SetPolicyAsync(owner, require: true, strong: true);
            (await StatusOfAsync(owner, "/libraries")).Should().Be(HttpStatusCode.OK, "a session already open carries on");
        }

        // The old password still signs in, but only to set a new one.
        using var login = await LoginAsync(factory, MediaManFactory.SuperadminUsername, MediaManFactory.SuperadminPassword);
        using var tokens = await JsonOfAsync(login);
        tokens.RootElement.GetProperty("passwordChangeRequired").GetBoolean().Should().BeTrue();
        using var client = ClientWith(factory, tokens);

        using (var libraries = await client.GetAsync(new Uri("/libraries", UriKind.Relative), Ct))
        {
            libraries.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            await ContractDocument.Instance.AssertConformsAsync(libraries, Ct);
            (await JsonOfAsync(libraries)).RootElement.GetProperty("code").GetString().Should().Be("auth.password_change_required");
        }

        (await AccountAsync(client)).Should().Be((true, true));
        (await SetPasswordAsync(client, null, "longbutweakpassword")).Should().Be("newPassword", "the strong rule applies");
        (await SetPasswordAsync(client, null, Strong)).Should().BeNull("the session proved the password at sign-in");

        (await AccountAsync(client)).Should().Be((true, false));
        (await StatusOfAsync(client, "/libraries")).Should().Be(HttpStatusCode.OK);
        (await StatusOfAsync(client, "/settings/password-policy")).Should().Be(HttpStatusCode.OK);
        using var again = await LoginAsync(factory, MediaManFactory.SuperadminUsername, Strong);
        (await JsonOfAsync(again)).RootElement.GetProperty("passwordChangeRequired").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Without_passwords_accounts_are_picked_and_turning_them_back_on_asks_for_one()
    {
        await using var factory = new MediaManFactory { CompleteSetup = false };
        using var setup = await factory.CreateClient().PostAsJsonAsync("/setup", new
        {
            username = "owner",
            mode = "local",
            library = new { path = Path.Combine(_root, "Movies"), contentType = "movies" },
            passwordPolicy = new { requirePasswords = false, strongPasswords = false },
        }, Ct);
        setup.StatusCode.Should().Be(HttpStatusCode.OK);
        await ContractDocument.Instance.AssertConformsAsync(setup, Ct);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            (await scope.ServiceProvider.GetRequiredService<MediaManDbContext>().Users.SingleAsync(Ct)).PasswordHash.Should().BeNull();
        }

        var viewer = await factory.CreateUserAsync(Ct, MediaMan.Core.Permissions.LibraryView);
        string viewerName;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            viewerName = (await scope.ServiceProvider.GetRequiredService<MediaManDbContext>().Users.SingleAsync(u => u.Id == viewer.UserId, Ct)).Username;
        }

        using (var options = await factory.CreateClient().GetAsync(new Uri("/auth/sign-in-options", UriKind.Relative), Ct))
        {
            await ContractDocument.Instance.AssertConformsAsync(options, Ct);
            using var body = await JsonOfAsync(options);
            body.RootElement.GetProperty("passwordsRequired").GetBoolean().Should().BeFalse();
            body.RootElement.GetProperty("accounts").EnumerateArray().Select(a => a.GetProperty("username").GetString())
                .Should().BeEquivalentTo("owner", viewerName);
        }

        using var owner = await SignedInAsync(factory, "owner", password: null);
        (await AccountAsync(owner)).Should().Be((false, false));
        using (var anyPassword = await LoginAsync(factory, "owner", "not checked"))
        {
            anyPassword.StatusCode.Should().Be(HttpStatusCode.OK, "the password is ignored while they're off");
        }

        using (var unknown = await LoginAsync(factory, "nobody", null))
        {
            unknown.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        await SetPolicyAsync(owner, require: true, strong: false);

        // The viewer kept their password; the owner, who has none, signs in by name and must set one.
        using (var wrong = await LoginAsync(factory, viewerName, "the wrong password"))
        {
            wrong.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        using var login = await LoginAsync(factory, "owner", null);
        using var tokens = await JsonOfAsync(login);
        tokens.RootElement.GetProperty("passwordChangeRequired").GetBoolean().Should().BeTrue();
        using var client = ClientWith(factory, tokens);
        (await StatusOfAsync(client, "/settings/password-policy")).Should().Be(HttpStatusCode.Forbidden);
        (await SetPasswordAsync(client, null, Strong)).Should().BeNull();
        (await StatusOfAsync(client, "/settings/password-policy")).Should().Be(HttpStatusCode.OK);

        using var withPassword = await LoginAsync(factory, "owner", Strong);
        (await JsonOfAsync(withPassword)).RootElement.GetProperty("passwordChangeRequired").GetBoolean().Should().BeFalse();
        using var withoutPassword = await LoginAsync(factory, "owner", null);
        withoutPassword.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "once set, the password is needed");
    }

    [Fact]
    public async Task Setup_checks_the_password_against_the_chosen_policy()
    {
        await using var factory = new MediaManFactory { CompleteSetup = false };
        using var client = factory.CreateClient();
        object Request(string? password, bool require, bool strong) => new
        {
            username = "owner",
            password,
            mode = "local",
            library = new { path = Path.Combine(_root, "Movies"), contentType = "movies" },
            passwordPolicy = new { requirePasswords = require, strongPasswords = strong },
        };

        foreach (var (password, require, strong) in new[] { ((string?)null, true, false), ("longbutweakpassword", true, true), ("short", false, false) })
        {
            using var refused = await client.PostAsJsonAsync("/setup", Request(password, require, strong), Ct);
            refused.StatusCode.Should().Be(HttpStatusCode.BadRequest, $"'{password}' with require {require}, strong {strong}");
            await ContractDocument.Instance.AssertConformsAsync(refused, Ct);
            (await JsonOfAsync(refused)).RootElement.GetProperty("errors").EnumerateObject().Select(e => e.Name).Should().Equal("password");
        }

        using var done = await client.PostAsJsonAsync("/setup", Request(Strong, require: true, strong: true), Ct);
        done.StatusCode.Should().Be(HttpStatusCode.OK);
        using var owner = await SignedInAsync(factory, "owner", Strong);
        (await PolicyAsync(owner)).Should().Be((true, true));
    }

    [Fact]
    public async Task Changing_a_password_needs_the_current_one()
    {
        await using var factory = new MediaManFactory();
        using var owner = await SignedInAsync(factory, MediaManFactory.SuperadminUsername, MediaManFactory.SuperadminPassword);

        (await SetPasswordAsync(owner, null, Strong)).Should().Be("currentPassword");
        (await SetPasswordAsync(owner, "not the password", Strong)).Should().Be("currentPassword");
        (await SetPasswordAsync(owner, MediaManFactory.SuperadminPassword, Strong)).Should().BeNull();

        using var old = await LoginAsync(factory, MediaManFactory.SuperadminUsername, MediaManFactory.SuperadminPassword);
        old.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        using var current = await LoginAsync(factory, MediaManFactory.SuperadminUsername, Strong);
        current.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static async Task<HttpResponseMessage> LoginAsync(MediaManFactory factory, string username, string? password)
    {
        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/auth/login", new { username, password }, Ct);
        await ContractDocument.Instance.AssertConformsAsync(response, Ct);
        return response;
    }

    private static async Task<HttpClient> SignedInAsync(MediaManFactory factory, string username, string? password)
    {
        using var login = await LoginAsync(factory, username, password);
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        using var tokens = await JsonOfAsync(login);
        return ClientWith(factory, tokens);
    }

    private static HttpClient ClientWith(MediaManFactory factory, JsonDocument tokens)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", tokens.RootElement.GetProperty("accessToken").GetString());
        return client;
    }

    private static async Task<(bool Require, bool Strong)> PolicyAsync(HttpClient client)
    {
        using var response = await client.GetAsync(new Uri("/settings/password-policy", UriKind.Relative), Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await ContractDocument.Instance.AssertConformsAsync(response, Ct);
        using var body = await JsonOfAsync(response);
        return (body.RootElement.GetProperty("requirePasswords").GetBoolean(), body.RootElement.GetProperty("strongPasswords").GetBoolean());
    }

    private static async Task SetPolicyAsync(HttpClient client, bool require, bool strong)
    {
        using var response = await client.PutAsJsonAsync("/settings/password-policy", new { requirePasswords = require, strongPasswords = strong }, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await ContractDocument.Instance.AssertConformsAsync(response, Ct);
    }

    /// <returns>(hasPassword, passwordChangeRequired)</returns>
    private static async Task<(bool HasPassword, bool MustChange)> AccountAsync(HttpClient client)
    {
        using var response = await client.GetAsync(new Uri("/account", UriKind.Relative), Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await ContractDocument.Instance.AssertConformsAsync(response, Ct);
        using var body = await JsonOfAsync(response);
        return (body.RootElement.GetProperty("hasPassword").GetBoolean(), body.RootElement.GetProperty("passwordChangeRequired").GetBoolean());
    }

    /// <returns>Null when the password was set; otherwise the field the <c>400</c> names.</returns>
    private static async Task<string?> SetPasswordAsync(HttpClient client, string? currentPassword, string newPassword)
    {
        using var response = await client.PutAsJsonAsync("/account/password", new { currentPassword, newPassword }, Ct);
        await ContractDocument.Instance.AssertConformsAsync(response, Ct);
        if (response.StatusCode == HttpStatusCode.NoContent)
        {
            return null;
        }

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var body = await JsonOfAsync(response);
        body.RootElement.GetProperty("code").GetString().Should().Be("account.password_invalid");
        return body.RootElement.GetProperty("errors").EnumerateObject().Single().Name;
    }

    private static async Task<HttpStatusCode> StatusOfAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(new Uri(path, UriKind.Relative), Ct);
        return response.StatusCode;
    }

    private static async Task<JsonDocument> JsonOfAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
}
