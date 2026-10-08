using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using MediaMan.Auth;
using MediaMan.Server.Tests.Conformance;
using Microsoft.Extensions.DependencyInjection;

namespace MediaMan.Server.Tests;

/// <summary><c>POST /auth/login</c>, <c>POST /auth/refresh</c> and <c>POST /auth/logout</c> (SPEC §7.11).</summary>
public sealed class AuthApiTests : IAsyncLifetime
{
    private const string Password = "a long password";

    private readonly MediaManFactory _factory = new();
    private HttpClient _client = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _client = _factory.CreateClient();
        await using var scope = _factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AccountService>().CreateSuperadminAsync("owner", Password, Ct);
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task Signing_in_and_refreshing_return_tokens_matching_the_contract()
    {
        using var login = await PostAsync("/auth/login", new { username = "OWNER", password = Password, deviceName = "Test PC" });

        login.StatusCode.Should().Be(HttpStatusCode.OK);
        await ContractDocument.Instance.AssertConformsAsync(login, Ct);
        var tokens = await login.Content.ReadFromJsonAsync<AuthTokens>(Ct);
        var claims = await _factory.Services.GetRequiredService<AccessTokens>().ValidateAsync(tokens!.AccessToken);
        claims.Should().NotBeNull();

        using var refresh = await PostAsync("/auth/refresh", new { refreshToken = tokens.RefreshToken });

        refresh.StatusCode.Should().Be(HttpStatusCode.OK);
        await ContractDocument.Instance.AssertConformsAsync(refresh, Ct);
        var next = await refresh.Content.ReadFromJsonAsync<AuthTokens>(Ct);
        next!.RefreshToken.Should().NotBe(tokens.RefreshToken);
    }

    [Fact]
    public async Task Wrong_credentials_are_a_401_problem()
    {
        using var response = await PostAsync("/auth/login", new { username = "owner", password = "not the password" });

        await AssertProblemAsync(response, HttpStatusCode.Unauthorized, "auth.invalid_credentials");
    }

    [Fact]
    public async Task A_reused_refresh_token_is_a_401_problem()
    {
        using var login = await PostAsync("/auth/login", new { username = "owner", password = Password });
        var tokens = await login.Content.ReadFromJsonAsync<AuthTokens>(Ct);
        (await PostAsync("/auth/refresh", new { refreshToken = tokens!.RefreshToken })).Dispose();

        using var reused = await PostAsync("/auth/refresh", new { refreshToken = tokens.RefreshToken });

        await AssertProblemAsync(reused, HttpStatusCode.Unauthorized, "auth.session_expired");
    }

    [Fact]
    public async Task Signing_out_ends_the_session_but_not_the_others()
    {
        var tokens = await SignInAsync();
        var other = await SignInAsync();
        using var signedIn = new HttpRequestMessage(HttpMethod.Post, new Uri("/auth/logout", UriKind.Relative));
        signedIn.Headers.Authorization = new("Bearer", tokens.AccessToken);

        using var logout = await _client.SendAsync(signedIn, Ct);

        logout.StatusCode.Should().Be(HttpStatusCode.NoContent);
        await ContractDocument.Instance.AssertConformsAsync(logout, Ct);
        using var afterwards = await GetLibrariesAsync(tokens.AccessToken);
        await AssertProblemAsync(afterwards, HttpStatusCode.Unauthorized, "auth.session_expired");
        using var refresh = await PostAsync("/auth/refresh", new { refreshToken = tokens.RefreshToken });
        await AssertProblemAsync(refresh, HttpStatusCode.Unauthorized, "auth.session_expired");
        using var otherSession = await GetLibrariesAsync(other.AccessToken);
        otherSession.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Signing_out_without_a_token_is_a_401_problem()
    {
        using var response = await _client.PostAsync(new Uri("/auth/logout", UriKind.Relative), null, Ct);

        await AssertProblemAsync(response, HttpStatusCode.Unauthorized, "auth.required");
    }

    [Theory]
    [InlineData("/auth/login", """{"username":"owner"}""")]
    [InlineData("/auth/login", """{"username":" ","password":"x"}""")]
    [InlineData("/auth/refresh", """{"refreshToken":""}""")]
    public async Task Missing_fields_are_a_400_problem(string path, string json)
    {
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _client.PostAsync(new Uri(path, UriKind.Relative), content, Ct);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "bad_request");
    }

    private async Task<AuthTokens> SignInAsync()
    {
        using var login = await PostAsync("/auth/login", new { username = "owner", password = Password });
        return (await login.Content.ReadFromJsonAsync<AuthTokens>(Ct))!;
    }

    private async Task<HttpResponseMessage> GetLibrariesAsync(string accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/libraries", UriKind.Relative));
        request.Headers.Authorization = new("Bearer", accessToken);
        return await _client.SendAsync(request, Ct);
    }

    private Task<HttpResponseMessage> PostAsync(string path, object body) =>
        _client.PostAsJsonAsync(new Uri(path, UriKind.Relative), body, Ct);

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        response.StatusCode.Should().Be(status);
        await ContractDocument.Instance.AssertConformsAsync(response, Ct);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        body.RootElement.GetProperty("code").GetString().Should().Be(code);
        body.RootElement.GetProperty("action").GetString().Should().NotBeNullOrWhiteSpace();
    }
}
