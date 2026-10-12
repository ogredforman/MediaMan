using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MediaMan.Data;
using MediaMan.Server.Settings;
using MediaMan.Server.Tests.Conformance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MediaMan.Server.Tests;

/// <summary>Settings → Metadata (SPEC §8 metadata [M], §11): the TMDB token override at <c>/settings/metadata</c>.</summary>
public sealed class SettingsApiTests : IDisposable
{
    private const string Token = "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJ0ZXN0In0.c2lnbmF0dXJl";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "mediaman-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Setting_and_clearing_the_token_changes_the_source_and_the_token_in_use()
    {
        await using var factory = new MediaManFactory();
        using var client = await factory.CreateSignedInClientAsync(Ct);
        var tokenOverride = factory.Services.GetRequiredService<TmdbTokenOverride>();

        (await SourceAsync(await client.GetAsync(new Uri("/settings/metadata", UriKind.Relative), Ct))).Should().Be("shipped");

        (await SourceAsync(await client.PutAsJsonAsync("/settings/metadata", new { tmdbAccessToken = $" {Token} " }, Ct)))
            .Should().Be("settings");
        tokenOverride.Token.Should().Be(Token, "the next TMDB request uses it, without a restart");
        (await SourceAsync(await client.GetAsync(new Uri("/settings/metadata", UriKind.Relative), Ct))).Should().Be("settings");

        (await SourceAsync(await client.PutAsJsonAsync("/settings/metadata", new { tmdbAccessToken = "" }, Ct))).Should().Be("shipped");
        tokenOverride.Token.Should().BeNull();
        await using var scope = factory.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<MediaManDbContext>().Settings
            .AnyAsync(s => s.Key == MetadataSettingsService.TmdbTokenKey, Ct)).Should().BeFalse();
    }

    [Fact]
    public async Task A_configured_token_is_reported_as_the_configuration()
    {
        await using var factory = new MediaManFactory(settings: new Dictionary<string, string>
        {
            ["MediaMan:Metadata:Tmdb:AccessToken"] = "configured-token",
        });
        using var client = await factory.CreateSignedInClientAsync(Ct);

        (await SourceAsync(await client.GetAsync(new Uri("/settings/metadata", UriKind.Relative), Ct))).Should().Be("configuration");
    }

    [Fact]
    public async Task The_token_is_stored_encrypted_never_returned_and_survives_a_restart()
    {
        var dataDirectory = Path.Combine(_root, "data");
        await using (var first = new MediaManFactory { DataDirectory = dataDirectory, KeepDataDirectory = true })
        {
            using var client = await first.CreateSignedInClientAsync(Ct);
            using var response = await client.PutAsJsonAsync("/settings/metadata", new { tmdbAccessToken = Token }, Ct);
            (await response.Content.ReadAsStringAsync(Ct)).Should().NotContain(Token);

            await using var scope = first.Services.CreateAsyncScope();
            var stored = await scope.ServiceProvider.GetRequiredService<MediaManDbContext>().Settings
                .SingleAsync(s => s.Key == MetadataSettingsService.TmdbTokenKey, Ct);
            stored.Value.Should().NotContain(Token).And.NotContain(Token[..20]);
        }

        await using var restarted = new MediaManFactory { DataDirectory = dataDirectory, CompleteSetup = false };
        using var restartedClient = restarted.CreateClient();
        using var health = await restartedClient.GetAsync(new Uri("/health", UriKind.Relative), Ct);

        restarted.Services.GetRequiredService<TmdbTokenOverride>().Token.Should().Be(Token);
    }

    [Fact]
    public async Task A_stored_token_that_cant_be_decrypted_falls_back_to_the_default()
    {
        var dataDirectory = Path.Combine(_root, "data");
        await using (var first = new MediaManFactory { DataDirectory = dataDirectory, KeepDataDirectory = true })
        {
            using var client = first.CreateClient();
            using var health = await client.GetAsync(new Uri("/health", UriKind.Relative), Ct);
            await using var scope = first.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<MediaManDbContext>();
            db.Settings.Add(new MediaMan.Core.Domain.Setting { Key = MetadataSettingsService.TmdbTokenKey, Value = "not-protected-by-this-key" });
            await db.SaveChangesAsync(Ct);
        }

        await using var restarted = new MediaManFactory { DataDirectory = dataDirectory, CompleteSetup = false };
        using var restartedClient = await restarted.CreateSignedInClientAsync(Ct);

        restarted.Services.GetRequiredService<TmdbTokenOverride>().Token.Should().BeNull();
        (await SourceAsync(await restartedClient.GetAsync(new Uri("/settings/metadata", UriKind.Relative), Ct))).Should().Be("shipped");
    }

    [Theory]
    [InlineData("two words")]
    [InlineData("0123456789abcdef0123456789abcdef")]
    [InlineData(null)]
    public async Task A_value_that_cant_be_a_read_access_token_is_refused(string? token)
    {
        await using var factory = new MediaManFactory();
        using var client = await factory.CreateSignedInClientAsync(Ct);

        using var response = await client.PutAsJsonAsync("/settings/metadata", new { tmdbAccessToken = token }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await ContractDocument.Instance.AssertConformsAsync(response, Ct);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("code").GetString().Should().Be("bad_request");
        body.GetProperty("errors").EnumerateObject().Select(p => p.Name).Should().Equal("tmdbAccessToken");
        factory.Services.GetRequiredService<TmdbTokenOverride>().Token.Should().BeNull();
    }

    private static async Task<string> SourceAsync(HttpResponseMessage response)
    {
        using (response)
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            await ContractDocument.Instance.AssertConformsAsync(response, Ct);
            return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("tmdbAccessTokenSource").GetString()!;
        }
    }
}
