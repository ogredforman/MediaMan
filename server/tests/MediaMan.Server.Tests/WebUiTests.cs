using System.Net;
using System.Text.Json;
using MediaMan.Server.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace MediaMan.Server.Tests;

/// <summary>
/// The server serves the built web UI under <c>/app/</c> next to the API (PLAN 2.5.2b, ADR 0009): its files and its
/// routes are reachable signed out and before setup, and nothing else changes for the API.
/// </summary>
public sealed class WebUiTests : IDisposable
{
    private const string IndexHtml = "<!doctype html><title>MediaMan</title><script src=\"/app/assets/index-abc123.js\"></script>";
    private const string Script = "console.log('MediaMan');";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "mediaman-tests", Guid.NewGuid().ToString("N"));

    public WebUiTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "web", "assets"));
        File.WriteAllText(Path.Combine(_root, "web", "index.html"), IndexHtml);
        File.WriteAllText(Path.Combine(_root, "web", "assets", "index-abc123.js"), Script);
        File.WriteAllText(Path.Combine(_root, "web", ".secret"), "hidden");
        File.WriteAllText(Path.Combine(_root, "outside.txt"), "outside the UI folder");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private MediaManFactory ServingUi(bool completeSetup = true) =>
        new(settings: new Dictionary<string, string> { ["MediaMan:Web:RootPath"] = Path.Combine(_root, "web") })
        {
            CompleteSetup = completeSetup,
        };

    private static HttpClient Client(MediaManFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [Theory]
    [InlineData("/app/")]
    [InlineData("/app/titles/0199a1b2-0000-7000-8000-000000000000")]
    [InlineData("/app/settings/metadata")]
    [InlineData("/app/sign-in?redirect=%2Fsearch")]
    public async Task The_app_routes_fall_back_to_its_main_page_signed_out(string path)
    {
        await using var factory = ServingUi();
        using var client = Client(factory);

        using var response = await client.GetAsync(new Uri(path, UriKind.Relative), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/html");
        response.Headers.CacheControl!.NoCache.Should().BeTrue();
        response.Headers.GetValues("X-Content-Type-Options").Should().Equal("nosniff");
        (await response.Content.ReadAsStringAsync(Ct)).Should().Be(IndexHtml);
    }

    [Fact]
    public async Task The_app_loads_before_setup_while_the_api_still_asks_for_it()
    {
        await using var factory = ServingUi(completeSetup: false);
        using var client = Client(factory);

        using var page = await client.GetAsync(new Uri("/app/setup", UriKind.Relative), Ct);
        using var api = await client.GetAsync(new Uri("/libraries", UriKind.Relative), Ct);

        page.StatusCode.Should().Be(HttpStatusCode.OK);
        (await page.Content.ReadAsStringAsync(Ct)).Should().Be(IndexHtml);
        await AssertProblemAsync(api, HttpStatusCode.Locked, "setup.required");
    }

    [Fact]
    public async Task Build_assets_are_served_with_a_long_cache()
    {
        await using var factory = ServingUi();
        using var client = Client(factory);

        using var response = await client.GetAsync(new Uri("/app/assets/index-abc123.js", UriKind.Relative), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/javascript");
        response.Headers.GetValues("Cache-Control").Should().Equal("public, max-age=31536000, immutable");
        response.Headers.GetValues("X-Content-Type-Options").Should().Equal("nosniff");
        (await response.Content.ReadAsStringAsync(Ct)).Should().Be(Script);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/app")]
    public async Task The_server_address_opens_the_app(string path)
    {
        await using var factory = ServingUi();
        using var client = Client(factory);

        using var response = await client.GetAsync(new Uri(path, UriKind.Relative), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Be("/app/");
    }

    [Theory]
    [InlineData("/app/assets/missing.js")]
    [InlineData("/app/.secret")]
    [InlineData("/app/%2E%2E/outside.txt")]
    [InlineData("/app/..%2Foutside.txt")]
    public async Task Missing_hidden_and_outside_files_are_not_found(string path)
    {
        await using var factory = ServingUi();
        using var client = Client(factory);

        using var response = await client.GetAsync(new Uri(path, UriKind.Relative), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync(Ct)).Should().NotContain("MediaMan</title>").And.NotContain("outside the UI folder").And.NotContain("hidden");
    }

    [Theory]
    [InlineData("/titles/0199a1b2-0000-7000-8000-000000000000")]
    [InlineData("/search?q=alien")]
    [InlineData("/profiles")]
    [InlineData("/settings/metadata")]
    public async Task Api_routes_that_share_a_path_with_the_app_keep_their_permission(string path)
    {
        await using var factory = ServingUi();
        using var client = Client(factory);

        using var response = await client.GetAsync(new Uri(path, UriKind.Relative), Ct);

        await AssertProblemAsync(response, HttpStatusCode.Unauthorized, "auth.required");
    }

    [Theory]
    [InlineData("GET", "/nope")]
    [InlineData("GET", "/api/titles")]
    [InlineData("POST", "/app/titles")]
    public async Task Unknown_paths_are_not_answered_with_the_app(string method, string path)
    {
        await using var factory = ServingUi();
        using var client = Client(factory);

        using var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), new Uri(path, UriKind.Relative)), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync(Ct)).Should().NotContain("MediaMan</title>");
    }

    [Fact]
    public async Task Outside_clients_are_refused_the_app_too()
    {
        await using var factory = ServingUi();
        using var client = Client(factory);
        client.DefaultRequestHeaders.Add(MediaManFactory.RemoteAddressHeader, "203.0.113.7");

        using var response = await client.GetAsync(new Uri("/app/", UriKind.Relative), Ct);

        await AssertProblemAsync(response, HttpStatusCode.Forbidden, "remote_access.disabled");
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/app/")]
    [InlineData("/app/titles/x")]
    public async Task Without_a_built_ui_only_the_api_is_served(string path)
    {
        await using var factory = new MediaManFactory();
        using var client = Client(factory);

        using var response = await client.GetAsync(new Uri(path, UriKind.Relative), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public void A_configured_folder_wins_over_the_bundled_one()
    {
        var configured = Path.Combine(_root, "elsewhere");

        WebUi.Resolve(configured, _root, _ => true).Should().Be(Path.GetFullPath(configured));
    }

    [Fact]
    public void The_bundled_folder_beside_the_server_is_the_default()
    {
        var bundled = Path.Combine(_root, "web");

        WebUi.Resolve(" ", _root, p => p == Path.Combine(bundled, "index.html")).Should().Be(bundled);
    }

    [Fact]
    public void Without_a_bundled_folder_no_ui_is_served() =>
        WebUi.Resolve(null, _root, _ => false).Should().BeNull();

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        response.StatusCode.Should().Be(status);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        body.RootElement.GetProperty("code").GetString().Should().Be(code);
    }
}
