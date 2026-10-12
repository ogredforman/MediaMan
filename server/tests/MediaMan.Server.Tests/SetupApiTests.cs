using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using MediaMan.Data;
using MediaMan.Server.Hosting;
using MediaMan.Server.Tests.Conformance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MediaMan.Server.Tests;

/// <summary>First-run setup (SPEC §7.1): the <c>423</c> gate, <c>POST /setup</c> and <c>PUT /server/mode</c>.</summary>
public sealed class SetupApiTests : IDisposable
{
    private const string Password = "a long setup password";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "mediaman-tests", Guid.NewGuid().ToString("N"));

    public SetupApiTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Movies", "The Matrix (1999)"));
        File.WriteAllText(Path.Combine(_root, "Movies", "The Matrix (1999)", "The Matrix (1999).mkv"), "placeholder");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string MoviesPath => Path.Combine(_root, "Movies");

    [Fact]
    public async Task Before_setup_every_route_but_health_and_setup_returns_423()
    {
        await using var factory = new MediaManFactory { CompleteSetup = false };
        var routes = await ContractCoverageTests.MappedRoutesAsync(factory);
        using var client = factory.CreateClient();

        foreach (var (method, template, _, _, _) in routes)
        {
            var path = template.Replace("{", "", StringComparison.Ordinal).Replace("}", "", StringComparison.Ordinal);
            using var request = new HttpRequestMessage(new HttpMethod(method), new Uri(path, UriKind.Relative));
            if (method != "GET")
            {
                request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
            }

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, Ct);
            if (template is "/health" or "/setup")
            {
                response.StatusCode.Should().NotBe(HttpStatusCode.Locked, $"{method} {template} answers before setup");
                continue;
            }

            response.StatusCode.Should().Be(HttpStatusCode.Locked, $"{method} {template} waits for setup");
            await ContractDocument.Instance.AssertConformsAsync(response, Ct);
            (await CodeOfAsync(response)).Should().Be("setup.required");
        }
    }

    [Fact]
    public async Task Setup_creates_the_Superadmin_records_the_mode_adds_the_library_and_signs_in()
    {
        await using var factory = new MediaManFactory { CompleteSetup = false };
        using var client = factory.CreateClient();
        var before = Snapshot(MoviesPath);

        using var response = await SetupAsync(client, Valid());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await ContractDocument.Instance.AssertConformsAsync(response, Ct);
        using var tokens = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", tokens.RootElement.GetProperty("accessToken").GetString());

        using var libraries = JsonDocument.Parse(await client.GetStringAsync(new Uri("/libraries", UriKind.Relative), Ct));
        var library = libraries.RootElement.GetProperty("items").EnumerateArray().Should().ContainSingle().Subject;
        library.GetProperty("name").GetString().Should().Be("Movies");
        library.GetProperty("kind").GetString().Should().Be("external");
        library.GetProperty("contentType").GetString().Should().Be("movies");
        library.GetProperty("rootPaths").EnumerateArray().Select(p => p.GetString()).Should().Equal(MoviesPath);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MediaManDbContext>();
        var user = await db.Users.SingleAsync(Ct);
        user.Username.Should().Be("owner");
        user.IsSuperadmin.Should().BeTrue();
        (await db.Settings.SingleAsync(s => s.Key == ServerModes.SettingKey, Ct)).Value.Should().Be("server");

        using var login = await client.PostAsJsonAsync("/auth/login", new { username = "owner", password = Password }, Ct);
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        Snapshot(MoviesPath).Should().Equal(before, "setup only reads an External library (SPEC §5)");
    }

    [Fact]
    public async Task Every_invalid_field_is_reported_by_name_and_nothing_is_created()
    {
        await using var factory = new MediaManFactory { CompleteSetup = false };
        using var client = factory.CreateClient();

        using var response = await SetupAsync(client, new
        {
            username = "  ",
            password = "too short",
            mode = "cloud",
            library = new { path = Path.Combine("relative", "Movies"), contentType = "music" },
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await ContractDocument.Instance.AssertConformsAsync(response, Ct);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        body.RootElement.GetProperty("code").GetString().Should().Be("setup.invalid");
        body.RootElement.GetProperty("errors").EnumerateObject().Select(p => p.Name)
            .Should().BeEquivalentTo("username", "password", "mode", "library.path", "library.contentType");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MediaManDbContext>();
        (await db.Users.AnyAsync(Ct)).Should().BeFalse();
        (await db.Libraries.AnyAsync(Ct)).Should().BeFalse();
        (await db.Settings.AnyAsync(Ct)).Should().BeFalse();
    }

    [Fact]
    public async Task A_missing_folder_or_library_is_reported_inline()
    {
        await using var factory = new MediaManFactory { CompleteSetup = false };
        using var client = factory.CreateClient();

        using var missingFolder = await SetupAsync(client, Valid(path: Path.Combine(_root, "Nowhere")));
        using var missingLibrary = await SetupAsync(client, new { username = "owner", password = Password, mode = "local" });

        (await ErrorsOfAsync(missingFolder)).Should().Equal("library.path");
        (await ErrorsOfAsync(missingLibrary)).Should().Equal("library");
    }

    [Fact]
    public async Task Setup_runs_once()
    {
        await using var factory = new MediaManFactory { CompleteSetup = false };
        using var client = factory.CreateClient();
        using var first = await SetupAsync(client, Valid());

        using var second = await SetupAsync(client, Valid(username: "intruder"));

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        await ContractDocument.Instance.AssertConformsAsync(second, Ct);
        (await CodeOfAsync(second)).Should().Be("setup.complete");
    }

    [Fact]
    public async Task Concurrent_setups_create_exactly_one_Superadmin()
    {
        await using var factory = new MediaManFactory { CompleteSetup = false };
        using var client = factory.CreateClient();

        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(i => SetupAsync(client, Valid(username: $"owner{i}"))));

        responses.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1);
        responses.Count(r => r.StatusCode == HttpStatusCode.Conflict).Should().Be(5);
        foreach (var response in responses)
        {
            response.Dispose();
        }

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MediaManDbContext>();
        (await db.Users.CountAsync(Ct)).Should().Be(1);
        (await db.Libraries.CountAsync(Ct)).Should().Be(1);
    }

    [Fact]
    public async Task The_recorded_mode_overrides_the_configured_one_after_a_restart()
    {
        var dataDirectory = Path.Combine(_root, "data");
        await using (var first = new MediaManFactory { CompleteSetup = false, DataDirectory = dataDirectory, KeepDataDirectory = true })
        {
            using var client = first.CreateClient();
            using var response = await SetupAsync(client, Valid(mode: "server"));
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            first.Services.GetRequiredService<ServerModes>().Active.Should().Be(ServerMode.Local, "a new mode takes effect on restart");
        }

        await using var restarted = new MediaManFactory(settings: new Dictionary<string, string> { ["MediaMan:Server:Mode"] = "Local" })
        {
            CompleteSetup = false,
            DataDirectory = dataDirectory,
        };
        using var restartedClient = restarted.CreateClient();
        using var health = await restartedClient.GetAsync(new Uri("/health", UriKind.Relative), Ct);

        var modes = restarted.Services.GetRequiredService<ServerModes>();
        modes.RecordedAtStartup.Should().Be(ServerMode.Server);
        modes.Active.Should().Be(ServerMode.Server);
        using var libraries = await restartedClient.GetAsync(new Uri("/libraries", UriKind.Relative), Ct);
        libraries.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "setup stays complete after a restart");
    }

    [Fact]
    public async Task Changing_the_mode_records_it_and_says_whether_a_restart_is_needed()
    {
        await using var factory = new MediaManFactory();
        using var client = await factory.CreateSignedInClientAsync(Ct);

        using var toServer = await client.PutAsJsonAsync("/server/mode", new { mode = "server" }, Ct);
        toServer.StatusCode.Should().Be(HttpStatusCode.OK);
        await ContractDocument.Instance.AssertConformsAsync(toServer, Ct);
        (await toServer.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("restartRequired").GetBoolean().Should().BeTrue();

        using var back = await client.PutAsJsonAsync("/server/mode", new { mode = "local" }, Ct);
        var state = await back.Content.ReadFromJsonAsync<JsonElement>(Ct);
        state.GetProperty("mode").GetString().Should().Be("local");
        state.GetProperty("restartRequired").GetBoolean().Should().BeFalse("the server is already running in Local mode");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MediaManDbContext>();
        (await db.Settings.SingleAsync(s => s.Key == ServerModes.SettingKey, Ct)).Value.Should().Be("local");
    }

    [Fact]
    public async Task Reading_the_mode_shows_the_recorded_one_and_whether_a_restart_is_needed()
    {
        await using var factory = new MediaManFactory();
        using var client = await factory.CreateSignedInClientAsync(Ct);

        using var before = await client.GetAsync(new Uri("/server/mode", UriKind.Relative), Ct);
        before.StatusCode.Should().Be(HttpStatusCode.OK);
        await ContractDocument.Instance.AssertConformsAsync(before, Ct);
        (await before.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("restartRequired").GetBoolean().Should().BeFalse();

        using var put = await client.PutAsJsonAsync("/server/mode", new { mode = "server" }, Ct);
        using var after = await client.GetAsync(new Uri("/server/mode", UriKind.Relative), Ct);
        var state = await after.Content.ReadFromJsonAsync<JsonElement>(Ct);
        state.GetProperty("mode").GetString().Should().Be("server");
        state.GetProperty("restartRequired").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task An_unknown_mode_is_refused()
    {
        await using var factory = new MediaManFactory();
        using var client = await factory.CreateSignedInClientAsync(Ct);

        using var response = await client.PutAsJsonAsync("/server/mode", new { mode = "cloud" }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await ContractDocument.Instance.AssertConformsAsync(response, Ct);
        (await ErrorsOfAsync(response)).Should().Equal("mode");
    }

    private object Valid(string username = "owner", string mode = "server", string? path = null) => new
    {
        username,
        password = Password,
        mode,
        library = new { path = path ?? MoviesPath, contentType = "movies" },
        deviceName = "Setup test",
    };

    private static Task<HttpResponseMessage> SetupAsync(HttpClient client, object body) =>
        client.PostAsJsonAsync("/setup", body, Ct);

    private static async Task<string?> CodeOfAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        return body.RootElement.GetProperty("code").GetString();
    }

    private static async Task<List<string>> ErrorsOfAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        return [.. body.RootElement.GetProperty("errors").EnumerateObject().Select(p => p.Name)];
    }

    private static List<string> Snapshot(string folder) =>
        [.. Directory.EnumerateFileSystemEntries(folder, "*", SearchOption.AllDirectories)
            .Select(p => $"{p}|{(File.Exists(p) ? new FileInfo(p).Length + "|" + File.GetLastWriteTimeUtc(p).Ticks : "dir")}")
            .Order(StringComparer.Ordinal)];
}
