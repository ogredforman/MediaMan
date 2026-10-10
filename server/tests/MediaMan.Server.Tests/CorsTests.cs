using System.Net;

namespace MediaMan.Server.Tests;

public sealed class CorsTests
{
    private const string UiOrigin = "http://localhost:5173";

    private static MediaManFactory WithAllowedOrigin() =>
        new(settings: new Dictionary<string, string> { ["MediaMan:AllowedOrigins:0"] = UiOrigin });

    private static HttpRequestMessage Preflight(string origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Options, new Uri("/profiles", UriKind.Relative));
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "GET");
        request.Headers.Add("Access-Control-Request-Headers", "authorization");
        return request;
    }

    [Fact]
    public async Task A_preflight_from_an_allowed_origin_is_answered_without_a_token()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = WithAllowedOrigin();
        using var client = factory.CreateClient();

        using var request = Preflight(UiOrigin);
        using var response = await client.SendAsync(request, ct);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        response.Headers.GetValues("Access-Control-Allow-Origin").Should().Equal(UiOrigin);
        response.Headers.GetValues("Access-Control-Allow-Headers").Single().Should().Contain("authorization");
        response.Headers.Contains("Access-Control-Allow-Credentials").Should().BeFalse();
    }

    [Fact]
    public async Task An_error_response_to_an_allowed_origin_stays_readable()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = WithAllowedOrigin();
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/profiles", UriKind.Relative));
        request.Headers.Add("Origin", UiOrigin);
        using var response = await client.SendAsync(request, ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.GetValues("Access-Control-Allow-Origin").Should().Equal(UiOrigin);
    }

    [Fact]
    public async Task Other_origins_get_no_cors_headers()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = WithAllowedOrigin();
        using var client = factory.CreateClient();

        using var request = Preflight("http://evil.example");
        using var response = await client.SendAsync(request, ct);

        response.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
    }

    [Fact]
    public async Task No_origin_is_allowed_by_default()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new MediaManFactory();
        using var client = factory.CreateClient();

        using var request = Preflight(UiOrigin);
        using var response = await client.SendAsync(request, ct);

        response.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
    }
}
