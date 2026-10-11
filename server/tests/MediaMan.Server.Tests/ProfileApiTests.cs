using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MediaMan.Auth;
using MediaMan.Server.Endpoints;
using MediaMan.Server.Tests.Conformance;

namespace MediaMan.Server.Tests;

/// <summary>Profiles and the session's pick (SPEC §7.11 step 3, §8 accounts): <c>/profiles</c> and <c>PUT /auth/session/profile</c>.</summary>
public sealed class ProfileApiTests : IAsyncLifetime
{
    private static readonly string[] _japaneseThenEnglish = ["ja", "en"];
    private static readonly string[] _english = ["en"];
    private static readonly string[] _unknownLanguage = ["xx"];
    private static readonly string?[] _englishThenNull = ["en", null];
    private static readonly string[] _sixLanguages = ["en", "fr", "de", "es", "it", "ja"];

    private readonly MediaManFactory _factory = new();
    private HttpClient _client = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync()
    {
        _client = _factory.CreateClient();
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task Setup_leaves_the_owner_with_one_profile_and_none_picked()
    {
        var tokens = await SignInAsync();

        using var response = await SendAsync(HttpMethod.Get, "/profiles", tokens.AccessToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await ContractDocument.Instance.AssertConformsAsync(response, Ct);
        var list = await ReadAsync<ProfileList>(response);
        list.Items.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Name = MediaManFactory.SuperadminUsername, Avatar = "violet" });
        list.ActiveProfileId.Should().BeNull();
    }

    [Fact]
    public async Task Creates_renames_and_deletes_profiles()
    {
        var token = (await SignInAsync()).AccessToken;

        using var created = await SendAsync(HttpMethod.Post, "/profiles", token, new { name = "Kids", avatar = "teal" });
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        await ContractDocument.Instance.AssertConformsAsync(created, Ct);
        var kids = await ReadAsync<ProfileDto>(created);
        created.Headers.Location!.ToString().Should().Be($"/profiles/{kids.Id}");

        using var updated = await SendAsync(HttpMethod.Patch, $"/profiles/{kids.Id}", token, new { name = "Children" });
        updated.StatusCode.Should().Be(HttpStatusCode.OK);
        await ContractDocument.Instance.AssertConformsAsync(updated, Ct);
        (await ReadAsync<ProfileDto>(updated)).Should().BeEquivalentTo(new { kids.Id, Name = "Children", Avatar = "teal" });

        using var languages = await SendAsync(HttpMethod.Patch, $"/profiles/{kids.Id}", token,
            new { audioLanguages = _japaneseThenEnglish, subtitleLanguages = _english });
        languages.StatusCode.Should().Be(HttpStatusCode.OK);
        await ContractDocument.Instance.AssertConformsAsync(languages, Ct);
        (await ReadAsync<ProfileDto>(languages)).Should().BeEquivalentTo(
            new { Name = "Children", AudioLanguages = _japaneseThenEnglish, SubtitleLanguages = _english }, o => o.WithStrictOrdering());

        using var deleted = await SendAsync(HttpMethod.Delete, $"/profiles/{kids.Id}", token);
        deleted.StatusCode.Should().Be(HttpStatusCode.NoContent);
        await ContractDocument.Instance.AssertConformsAsync(deleted, Ct);

        using var list = await SendAsync(HttpMethod.Get, "/profiles", token);
        (await ReadAsync<ProfileList>(list)).Items.Should().ContainSingle().Which.Name.Should().Be(MediaManFactory.SuperadminUsername);
    }

    [Fact]
    public async Task Rule_breaks_are_contract_problems()
    {
        var token = (await SignInAsync()).AccessToken;
        var only = await OnlyProfileAsync(token);

        await AssertProblemAsync(await SendAsync(HttpMethod.Post, "/profiles", token, new { name = " ", avatar = "neon" }),
            HttpStatusCode.BadRequest, "profile.invalid", fields: ["name", "avatar"]);
        await AssertProblemAsync(await SendAsync(HttpMethod.Post, "/profiles", token, new { name = MediaManFactory.SuperadminUsername.ToUpperInvariant() }),
            HttpStatusCode.Conflict, "profile.name_taken");
        await AssertProblemAsync(await SendAsync(HttpMethod.Delete, $"/profiles/{only.Id}", token), HttpStatusCode.Conflict, "profile.last");
        await AssertProblemAsync(await SendAsync(HttpMethod.Patch, $"/profiles/{Guid.NewGuid()}", token, new { name = "Ghost" }),
            HttpStatusCode.NotFound, "profile.not_found");
        await AssertProblemAsync(await SendAsync(HttpMethod.Patch, $"/profiles/{only.Id}", token,
                new { audioLanguages = _unknownLanguage, subtitleLanguages = _sixLanguages }),
            HttpStatusCode.BadRequest, "profile.invalid", fields: ["audioLanguages", "subtitleLanguages"]);
        await AssertProblemAsync(await SendAsync(HttpMethod.Patch, $"/profiles/{only.Id}", token, new { audioLanguages = _englishThenNull }),
            HttpStatusCode.BadRequest, "profile.invalid", fields: ["audioLanguages"]);

        for (var i = 1; i < ProfileService.MaximumProfiles; i++)
        {
            using var created = await SendAsync(HttpMethod.Post, "/profiles", token, new { name = $"Profile {i}" });
            created.StatusCode.Should().Be(HttpStatusCode.Created);
        }

        await AssertProblemAsync(await SendAsync(HttpMethod.Post, "/profiles", token, new { name = "One too many" }),
            HttpStatusCode.Conflict, "profile.limit");
    }

    [Fact]
    public void The_contract_offers_exactly_the_languages_the_server_accepts()
    {
        var codes = ContractDocument.Instance.Root["components"]!["schemas"]!["LanguageCode"]!["enum"]!.AsArray()
            .Select(code => code!.GetValue<string>());

        codes.Should().Equal(MediaMan.Core.Languages.Codes);
    }

    [Fact]
    public async Task Another_accounts_profiles_are_invisible()
    {
        var owner = (await SignInAsync()).AccessToken;
        var ownersProfile = await OnlyProfileAsync(owner);
        var other = await _factory.CreateUserAsync(Ct);

        using var list = await SendAsync(HttpMethod.Get, "/profiles", other.AccessToken);
        (await ReadAsync<ProfileList>(list)).Items.Should().ContainSingle().Which.Id.Should().NotBe(ownersProfile.Id);

        await AssertProblemAsync(await SendAsync(HttpMethod.Patch, $"/profiles/{ownersProfile.Id}", other.AccessToken, new { name = "Mine" }),
            HttpStatusCode.NotFound, "profile.not_found");
        await AssertProblemAsync(await SendAsync(HttpMethod.Delete, $"/profiles/{ownersProfile.Id}", other.AccessToken),
            HttpStatusCode.NotFound, "profile.not_found");
        await AssertProblemAsync(await SendAsync(HttpMethod.Put, "/auth/session/profile", other.AccessToken, new { profileId = ownersProfile.Id }),
            HttpStatusCode.NotFound, "profile.not_found");
    }

    [Fact]
    public async Task The_pick_belongs_to_the_session_and_survives_a_refresh()
    {
        var tv = await SignInAsync();
        var kids = await ReadAsync<ProfileDto>(await SendAsync(HttpMethod.Post, "/profiles", tv.AccessToken, new { name = "Kids" }));

        using var picked = await SendAsync(HttpMethod.Put, "/auth/session/profile", tv.AccessToken, new { profileId = kids.Id });
        picked.StatusCode.Should().Be(HttpStatusCode.OK);
        await ContractDocument.Instance.AssertConformsAsync(picked, Ct);
        (await ReadAsync<ProfileDto>(picked)).Id.Should().Be(kids.Id);

        using var refreshResponse = await _client.PostAsJsonAsync(new Uri("/auth/refresh", UriKind.Relative), new { refreshToken = tv.RefreshToken }, Ct);
        var refreshed = await ReadAsync<AuthTokens>(refreshResponse);
        (await ActiveProfileAsync(refreshed.AccessToken)).Should().Be(kids.Id);

        var phone = await SignInAsync();
        (await ActiveProfileAsync(phone.AccessToken)).Should().BeNull("a new sign-in starts at the picker");

        using var deleted = await SendAsync(HttpMethod.Delete, $"/profiles/{kids.Id}", phone.AccessToken);
        (await ActiveProfileAsync(refreshed.AccessToken)).Should().BeNull("deleting the picked profile sends the session back to the picker");
    }

    [Fact]
    public async Task Picking_without_a_profile_is_a_400_problem()
    {
        var token = (await SignInAsync()).AccessToken;

        await AssertProblemAsync(await SendAsync(HttpMethod.Put, "/auth/session/profile", token, new { }), HttpStatusCode.BadRequest, "bad_request");
    }

    private async Task<Guid?> ActiveProfileAsync(string accessToken)
    {
        using var response = await SendAsync(HttpMethod.Get, "/profiles", accessToken);
        return (await ReadAsync<ProfileList>(response)).ActiveProfileId;
    }

    private async Task<ProfileDto> OnlyProfileAsync(string accessToken)
    {
        using var response = await SendAsync(HttpMethod.Get, "/profiles", accessToken);
        return (await ReadAsync<ProfileList>(response)).Items.Single();
    }

    private async Task<AuthTokens> SignInAsync()
    {
        using var login = await _client.PostAsJsonAsync(new Uri("/auth/login", UriKind.Relative),
            new { username = MediaManFactory.SuperadminUsername, password = MediaManFactory.SuperadminPassword }, Ct);
        return await ReadAsync<AuthTokens>(login);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string accessToken, object? body = null)
    {
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await _client.SendAsync(request, Ct);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<T>(JsonSerializerOptions.Web, Ct))!;

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code, string[]? fields = null)
    {
        using (response)
        {
            response.StatusCode.Should().Be(status);
            await ContractDocument.Instance.AssertConformsAsync(response, Ct);
            using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
            problem.RootElement.GetProperty("code").GetString().Should().Be(code);
            if (fields is not null)
            {
                problem.RootElement.GetProperty("errors").EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(fields);
            }
        }
    }
}
