using System.Net;
using System.Text.Json;
using MediaMan.Server.Hosting;
using MediaMan.Server.Tests.Conformance;

namespace MediaMan.Server.Tests;

/// <summary>Safety-critical (SPEC §12): with remote access off, clients outside the local network are refused.</summary>
public sealed class LocalNetworkGuardTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("127.255.0.9", true)]
    [InlineData("::1", true)]
    [InlineData("10.0.0.1", true)]
    [InlineData("10.255.255.255", true)]
    [InlineData("172.16.0.1", true)]
    [InlineData("172.31.255.255", true)]
    [InlineData("172.15.255.255", false)]
    [InlineData("172.32.0.1", false)]
    [InlineData("192.168.1.20", true)]
    [InlineData("192.169.0.1", false)]
    [InlineData("169.254.10.1", true)]
    [InlineData("100.64.0.1", true)]
    [InlineData("100.127.255.255", true)]
    [InlineData("100.63.255.255", false)]
    [InlineData("100.128.0.1", false)]
    [InlineData("fd00::1", true)]
    [InlineData("fc00::1", true)]
    [InlineData("fe80::1", true)]
    [InlineData("fe80::1%12", true)]
    [InlineData("fd7a:115c:a1e0::1", true)]
    [InlineData("::ffff:192.168.0.5", true)]
    [InlineData("::ffff:8.8.8.8", false)]
    [InlineData("8.8.8.8", false)]
    [InlineData("1.1.1.1", false)]
    [InlineData("0.0.0.0", false)]
    [InlineData("::", false)]
    [InlineData("2001:4860:4860::8888", false)]
    [InlineData("fec0::1", false)]
    public void Local_network_ranges(string address, bool local) =>
        LocalNetwork.IsLocal(IPAddress.Parse(address)).Should().Be(local);

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("192.168.1.20")]
    [InlineData("100.100.1.2")]
    [InlineData("fe80::1")]
    public async Task Local_clients_are_served(string address)
    {
        await using var factory = new MediaManFactory();
        using var response = await GetHealthAsync(factory, address);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("2001:4860:4860::8888")]
    [InlineData("none")]
    public async Task Outside_or_unknown_clients_are_refused_even_for_health(string address)
    {
        await using var factory = new MediaManFactory();
        using var response = await GetHealthAsync(factory, address);

        await AssertRefusedAsync(response);
    }

    [Fact]
    public async Task Requests_forwarded_by_a_proxy_on_the_same_machine_are_refused()
    {
        await using var factory = new MediaManFactory();
        foreach (var header in LocalNetworkGuard.ForwardingHeaders)
        {
            using var client = factory.CreateClient();
            client.DefaultRequestHeaders.TryAddWithoutValidation(header, header == "Forwarded" ? "for=127.0.0.1" : "127.0.0.1");

            using var response = await client.GetAsync(new Uri("/health", UriKind.Relative), Ct);

            await AssertRefusedAsync(response);
        }
    }

    [Fact]
    public async Task Outside_clients_are_refused_before_authentication()
    {
        await using var factory = new MediaManFactory();
        var user = await factory.CreateUserAsync(Ct, [.. MediaMan.Core.Permissions.All]);
        using var client = factory.CreateClient(user);
        client.DefaultRequestHeaders.Add(MediaManFactory.RemoteAddressHeader, "203.0.113.7");

        using var response = await client.GetAsync(new Uri("/libraries", UriKind.Relative), Ct);

        await AssertRefusedAsync(response);
    }

    private static async Task<HttpResponseMessage> GetHealthAsync(MediaManFactory factory, string address)
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(MediaManFactory.RemoteAddressHeader, address);
        return await client.GetAsync(new Uri("/health", UriKind.Relative), Ct);
    }

    private static async Task AssertRefusedAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await ContractDocument.Instance.AssertConformsAsync(response, Ct);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        body.RootElement.GetProperty("code").GetString().Should().Be("remote_access.disabled");
        body.RootElement.GetProperty("action").GetString().Should().NotBeNullOrWhiteSpace();
    }
}
