using System.Net;
using System.Text.Json;
using MediaMan.Server.Tests.Conformance;

namespace MediaMan.Server.Tests;

public sealed class HealthTests
{
    [Fact]
    public async Task Health_is_healthy_without_a_token_and_matches_the_contract()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new MediaManFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/health", UriKind.Relative), ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await ContractDocument.Instance.AssertConformsAsync(response, ct);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        body.RootElement.GetProperty("status").GetString().Should().Be("healthy");
    }

    [Fact]
    public async Task Health_reports_the_app_version_from_the_root_package_json()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new MediaManFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/health", UriKind.Relative), ct);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        body.RootElement.GetProperty("version").GetString().Should().Be(await RootPackageVersionAsync(ct));
    }

    /// <summary>The <c>version</c> in the repository's root package.json, the app version's single source (PLAN 2.5.5).</summary>
    private static async Task<string> RootPackageVersionAsync(CancellationToken ct)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var packageJson = Path.Combine(dir.FullName, "package.json");
            if (File.Exists(packageJson) && File.Exists(Path.Combine(dir.FullName, "server", "MediaMan.sln")))
            {
                using var package = JsonDocument.Parse(await File.ReadAllTextAsync(packageJson, ct));
                return package.RootElement.GetProperty("version").GetString()!;
            }
        }
        throw new InvalidOperationException("The repository root (package.json beside server/MediaMan.sln) wasn't found.");
    }
}
