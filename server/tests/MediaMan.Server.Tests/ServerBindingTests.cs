using MediaMan.Server.Hosting;

namespace MediaMan.Server.Tests;

public sealed class ServerBindingTests
{
    [Fact]
    public void Local_mode_listens_on_loopback_only() =>
        ServerBinding.Plan(new ServerOptions { Mode = ServerMode.Local, Port = 5080 })
            .Should().Be(new ListenPlan(Loopback: true, 5080, "http"));

    [Fact]
    public void Server_mode_listens_on_every_interface() =>
        ServerBinding.Plan(new ServerOptions { Mode = ServerMode.Server })
            .Should().Be(new ListenPlan(Loopback: false, ServerOptions.DefaultPort, "http"));

    [Theory]
    [InlineData(ServerMode.Local, ServerMode.Server, false)]
    [InlineData(ServerMode.Server, ServerMode.Local, true)]
    public void The_mode_setup_recorded_wins_over_the_configured_one(ServerMode configured, ServerMode recorded, bool loopback) =>
        ServerBinding.Plan(new ServerOptions { Mode = configured }, recorded).Loopback.Should().Be(loopback);

    [Fact]
    public void Local_is_the_default() =>
        ServerBinding.Plan(new ServerOptions()).Loopback.Should().BeTrue();

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    public void An_invalid_port_says_how_to_fix_it(int port)
    {
        var act = () => ServerBinding.Plan(new ServerOptions { Port = port });

        act.Should().Throw<InvalidOperationException>().WithMessage("*MediaMan:Server:Port*");
    }
}
