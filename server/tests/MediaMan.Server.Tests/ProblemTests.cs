using System.Net;
using System.Text.Json;
using MediaMan.Server.Hosting;
using MediaMan.Server.Tests.Conformance;

namespace MediaMan.Server.Tests;

public sealed class ProblemTests
{
    [Fact]
    public async Task Unknown_route_returns_a_problem_that_matches_the_error_model()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new MediaManFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/no-such-route", UriKind.Relative), ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        ContractDocument.Instance.Validate(ContractDocument.ComponentSchema("Problem"), body.RootElement).Should().BeEmpty();
        body.RootElement.GetProperty("code").GetString().Should().Be("not_found");
        body.RootElement.GetProperty("action").GetString().Should().NotBeNullOrWhiteSpace();
        body.RootElement.GetProperty("instance").GetString().Should().Be("/no-such-route");
    }

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(405)]
    [InlineData(409)]
    [InlineData(418)]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(503)]
    public void Every_error_status_gets_a_contract_valid_code_and_an_action(int status)
    {
        var problem = JsonSerializer.SerializeToElement(new Dictionary<string, object>
        {
            ["title"] = "Something failed",
            ["status"] = status,
            ["code"] = Problems.CodeFor(status),
            ["action"] = Problems.ActionFor(status),
        });

        ContractDocument.Instance.Validate(ContractDocument.ComponentSchema("Problem"), problem).Should().BeEmpty();
    }
}
