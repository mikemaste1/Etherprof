using Etherprof.Contracts.Models;
using Etherprof.Core;
using Xunit;

namespace Etherprof.Core.Tests;

public class ProfileMatcherTests
{
    private static NetworkAdapterState CreateState(
        bool isAvailable = true,
        bool isDhcp = false,
        bool isDnsAutomatic = true,
        string? ipv4 = null,
        byte? prefix = null,
        string? gateway = null,
        string[]? dnsServers = null)
    {
        return new NetworkAdapterState
        {
            AdapterId = "test-adapter",
            IsAvailable = isAvailable,
            IsConnected = true,
            IsDhcpEnabled = isDhcp,
            IsDnsAutomatic = isDnsAutomatic,
            IPv4Address = ipv4,
            PrefixLength = prefix,
            Gateway = gateway,
            DnsServers = dnsServers ?? [],
            LinkSpeed = "1 Gbps"
        };
    }

    [Fact]
    public void DhcpProfile_MatchesDhcpState()
    {
        var state = CreateState(isDhcp: true);
        var profile = new NetworkProfile
        {
            Id = Guid.NewGuid(),
            Name = "DHCP",
            Type = NetworkProfileType.Dhcp,
            ApplyDns = false
        };

        Assert.True(ProfileMatcher.IsMatch(state, profile));
    }

    [Fact]
    public void DhcpProfile_DoesNotMatchStaticState()
    {
        var state = CreateState(isDhcp: false, ipv4: "192.168.1.10", prefix: 24);
        var profile = new NetworkProfile
        {
            Id = Guid.NewGuid(),
            Name = "DHCP",
            Type = NetworkProfileType.Dhcp,
            ApplyDns = false
        };

        Assert.False(ProfileMatcher.IsMatch(state, profile));
    }

    [Fact]
    public void StaticProfile_MatchesWhenIpAndPrefixMatch()
    {
        var state = CreateState(isDhcp: false, ipv4: "192.168.88.10", prefix: 24);
        var profile = new NetworkProfile
        {
            Id = Guid.NewGuid(),
            Name = "MikroTik",
            Type = NetworkProfileType.Static,
            IPv4 = new IPv4Configuration { Address = "192.168.88.10", PrefixLength = 24 },
            ApplyDns = false
        };

        Assert.True(ProfileMatcher.IsMatch(state, profile));
    }

    [Fact]
    public void StaticProfile_DoesNotMatchDifferentIp()
    {
        var state = CreateState(isDhcp: false, ipv4: "192.168.88.11", prefix: 24);
        var profile = new NetworkProfile
        {
            Id = Guid.NewGuid(),
            Name = "MikroTik",
            Type = NetworkProfileType.Static,
            IPv4 = new IPv4Configuration { Address = "192.168.88.10", PrefixLength = 24 },
            ApplyDns = false
        };

        Assert.False(ProfileMatcher.IsMatch(state, profile));
    }

    [Fact]
    public void StaticProfile_DoesNotMatchDifferentPrefix()
    {
        var state = CreateState(isDhcp: false, ipv4: "192.168.88.10", prefix: 16);
        var profile = new NetworkProfile
        {
            Id = Guid.NewGuid(),
            Name = "MikroTik",
            Type = NetworkProfileType.Static,
            IPv4 = new IPv4Configuration { Address = "192.168.88.10", PrefixLength = 24 },
            ApplyDns = false
        };

        Assert.False(ProfileMatcher.IsMatch(state, profile));
    }

    [Fact]
    public void StaticProfile_GatewayComparedOnlyWhenProfileSpecifiesOne()
    {
        var state = CreateState(isDhcp: false, ipv4: "10.0.0.5", prefix: 24, gateway: "10.0.0.1");
        var profileNoGw = new NetworkProfile
        {
            Id = Guid.NewGuid(),
            Name = "NoGW",
            Type = NetworkProfileType.Static,
            IPv4 = new IPv4Configuration { Address = "10.0.0.5", PrefixLength = 24, Gateway = null },
            ApplyDns = false
        };
        var profileWithGw = new NetworkProfile
        {
            Id = Guid.NewGuid(),
            Name = "WithGW",
            Type = NetworkProfileType.Static,
            IPv4 = new IPv4Configuration { Address = "10.0.0.5", PrefixLength = 24, Gateway = "10.0.0.1" },
            ApplyDns = false
        };
        var profileWrongGw = new NetworkProfile
        {
            Id = Guid.NewGuid(),
            Name = "WrongGW",
            Type = NetworkProfileType.Static,
            IPv4 = new IPv4Configuration { Address = "10.0.0.5", PrefixLength = 24, Gateway = "10.0.0.254" },
            ApplyDns = false
        };

        Assert.True(ProfileMatcher.IsMatch(state, profileNoGw)); // no gateway in profile -> matches regardless
        Assert.True(ProfileMatcher.IsMatch(state, profileWithGw)); // matching gateway
        Assert.False(ProfileMatcher.IsMatch(state, profileWrongGw)); // different gateway
    }

    [Fact]
    public void ApplyDnsFalse_DnsIgnored()
    {
        var state = CreateState(isDhcp: true, isDnsAutomatic: false, dnsServers: ["8.8.8.8"]);
        var profile = new NetworkProfile
        {
            Id = Guid.NewGuid(),
            Name = "DHCP no DNS",
            Type = NetworkProfileType.Dhcp,
            ApplyDns = false
        };

        Assert.True(ProfileMatcher.IsMatch(state, profile)); // DNS ignored
    }

    [Fact]
    public void ApplyDnsTrue_AutomaticMode_MatchesWhenDnsAutomatic()
    {
        var state = CreateState(isDhcp: true, isDnsAutomatic: true);
        var profile = new NetworkProfile
        {
            Id = Guid.NewGuid(),
            Name = "DHCP with auto DNS",
            Type = NetworkProfileType.Dhcp,
            ApplyDns = true,
            Dns = new DnsConfiguration { Mode = DnsMode.Automatic }
        };

        Assert.True(ProfileMatcher.IsMatch(state, profile));
    }

    [Fact]
    public void ApplyDnsTrue_AutomaticMode_DoesNotMatchWhenDnsStatic()
    {
        var state = CreateState(isDhcp: true, isDnsAutomatic: false, dnsServers: ["8.8.8.8"]);
        var profile = new NetworkProfile
        {
            Id = Guid.NewGuid(),
            Name = "DHCP with auto DNS",
            Type = NetworkProfileType.Dhcp,
            ApplyDns = true,
            Dns = new DnsConfiguration { Mode = DnsMode.Automatic }
        };

        Assert.False(ProfileMatcher.IsMatch(state, profile));
    }

    [Fact]
    public void ApplyDnsTrue_StaticMode_MatchesWhenServersMatch()
    {
        var state = CreateState(isDhcp: false, isDnsAutomatic: false, ipv4: "10.0.0.5", prefix: 24, dnsServers: ["8.8.8.8", "1.1.1.1"]);
        var profile = new NetworkProfile
        {
            Id = Guid.NewGuid(),
            Name = "Static with DNS",
            Type = NetworkProfileType.Static,
            IPv4 = new IPv4Configuration { Address = "10.0.0.5", PrefixLength = 24 },
            ApplyDns = true,
            Dns = new DnsConfiguration { Mode = DnsMode.Static, Servers = ["1.1.1.1", "8.8.8.8"] }
        };

        Assert.True(ProfileMatcher.IsMatch(state, profile)); // order-insensitive
    }

    [Fact]
    public void ApplyDnsTrue_StaticMode_DoesNotMatchDifferentServers()
    {
        var state = CreateState(isDhcp: false, isDnsAutomatic: false, ipv4: "10.0.0.5", prefix: 24, dnsServers: ["8.8.8.8"]);
        var profile = new NetworkProfile
        {
            Id = Guid.NewGuid(),
            Name = "Static with DNS",
            Type = NetworkProfileType.Static,
            IPv4 = new IPv4Configuration { Address = "10.0.0.5", PrefixLength = 24 },
            ApplyDns = true,
            Dns = new DnsConfiguration { Mode = DnsMode.Static, Servers = ["1.1.1.1"] }
        };

        Assert.False(ProfileMatcher.IsMatch(state, profile));
    }

    [Fact]
    public void UnavailableAdapter_NeverMatches()
    {
        var state = CreateState(isAvailable: false);
        var profile = new NetworkProfile
        {
            Id = Guid.NewGuid(),
            Name = "DHCP",
            Type = NetworkProfileType.Dhcp,
            ApplyDns = false
        };

        Assert.False(ProfileMatcher.IsMatch(state, profile));
    }
}
