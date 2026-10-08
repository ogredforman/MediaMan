using System.Net;

namespace MediaMan.Server.Hosting;

/// <summary>
/// The local network that may reach the server while remote access is off (SPEC §7.12 step 1): loopback, private LAN
/// ranges (RFC 1918, IPv4 and IPv6 link-local, IPv6 unique-local) and the Tailscale ranges.
/// </summary>
public static class LocalNetwork
{
    private static readonly IPNetwork[] _ranges =
    [
        IPNetwork.Parse("10.0.0.0/8"),
        IPNetwork.Parse("172.16.0.0/12"),
        IPNetwork.Parse("192.168.0.0/16"),
        IPNetwork.Parse("169.254.0.0/16"),
        IPNetwork.Parse("100.64.0.0/10"), // Tailscale (CGNAT space)
        IPNetwork.Parse("fc00::/7"),
        IPNetwork.Parse("fe80::/10"),
        IPNetwork.Parse("fd7a:115c:a1e0::/48"), // Tailscale; inside fc00::/7, listed so the rule is explicit
    ];

    /// <summary>Whether <paramref name="address"/> is on the local network. IPv4-mapped IPv6 addresses count as IPv4.</summary>
    public static bool IsLocal(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        // Only the address bytes decide; a link-local address's scope id (fe80::1%12) is dropped first.
        var unscoped = address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 && address.ScopeId != 0
            ? new IPAddress(address.GetAddressBytes())
            : address;
        return _ranges.Any(r => r.Contains(unscoped));
    }
}
