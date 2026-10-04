using MediaMan.Core.Modules;

namespace MediaMan.Core.Tests;

public class Socks5ProxyTests
{
    [Fact]
    public void ToString_never_contains_the_password()
    {
        var proxy = new Socks5Proxy("proxy.example", 1080, "user", "hunter2-secret");

        proxy.ToString().Should().NotContain("hunter2-secret").And.Contain("***");
    }

    [Fact]
    public void Network_config_ToString_never_contains_the_password()
    {
        var net = new TorrentNetworkConfig(Domain.VpnMode.Socks5, null, new Socks5Proxy("proxy.example", 1080, "user", "hunter2-secret"));

        net.ToString().Should().NotContain("hunter2-secret");
    }
}
