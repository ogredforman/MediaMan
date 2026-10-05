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
}
