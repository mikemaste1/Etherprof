namespace Etherprof.Core;

using Etherprof.Contracts.Models;

public static class ProfileMatcher
{
    /// <summary>
    /// Determines whether the given adapter state matches the given profile.
    /// Returns true if the adapter's current configuration matches what the profile would apply.
    /// </summary>
    public static bool IsMatch(NetworkAdapterState state, NetworkProfile profile)
    {
        if (!state.IsAvailable) return false;

        // Check IP configuration match
        bool ipMatch = profile.Type switch
        {
            NetworkProfileType.Dhcp => state.IsDhcpEnabled,
            NetworkProfileType.Static => IsStaticIpMatch(state, profile.IPv4),
            NetworkProfileType.DnsOnly => true, // DNS Only profile does not check IP configuration
            _ => false
        };

        if (!ipMatch) return false;

        // DNS comparison only if ApplyDns is true
        if (profile.ApplyDns && profile.Dns is not null)
        {
            if (!IsDnsMatch(state, profile.Dns))
                return false;
        }

        return true;
    }

    private static bool IsStaticIpMatch(NetworkAdapterState state, IPv4Configuration? ipv4)
    {
        if (ipv4 is null) return false;
        if (state.IsDhcpEnabled) return false;

        // Address and prefix must match
        if (!string.Equals(state.IPv4Address, ipv4.Address, StringComparison.OrdinalIgnoreCase))
            return false;

        if (state.PrefixLength != ipv4.PrefixLength)
            return false;

        // Gateway: compare only if profile specifies one
        if (!string.IsNullOrEmpty(ipv4.Gateway))
        {
            if (!string.Equals(state.Gateway, ipv4.Gateway, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    private static bool IsDnsMatch(NetworkAdapterState state, DnsConfiguration dns)
    {
        return dns.Mode switch
        {
            DnsMode.Automatic => state.IsDnsAutomatic,
            DnsMode.Static => IsDnsServersMatch(state.DnsServers, dns.Servers),
            _ => false
        };
    }

    private static bool IsDnsServersMatch(IReadOnlyList<string> adapterServers, List<string> profileServers)
    {
        if (adapterServers.Count != profileServers.Count) return false;

        // Order-insensitive comparison
        var adapterSet = new HashSet<string>(adapterServers, StringComparer.OrdinalIgnoreCase);
        var profileSet = new HashSet<string>(profileServers, StringComparer.OrdinalIgnoreCase);

        return adapterSet.SetEquals(profileSet);
    }
}
