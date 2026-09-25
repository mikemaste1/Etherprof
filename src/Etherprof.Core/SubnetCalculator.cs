namespace Etherprof.Core;

using System.Net;

public static class SubnetCalculator
{
    /// <summary>
    /// Computes the network address CIDR string (e.g. "192.168.1.0/24") for a given IP and prefix length.
    /// </summary>
    public static string GetNetworkCidr(string ipAddress, byte prefixLength)
    {
        if (!IPAddress.TryParse(ipAddress, out var ip)) return $"{ipAddress}/{prefixLength}";

        byte[] ipBytes = ip.GetAddressBytes();
        if (ipBytes.Length != 4) return $"{ipAddress}/{prefixLength}";

        uint ipInt = ((uint)ipBytes[0] << 24) | ((uint)ipBytes[1] << 16) | ((uint)ipBytes[2] << 8) | (uint)ipBytes[3];
        uint mask = prefixLength == 0 ? 0 : 0xFFFFFFFF << (32 - prefixLength);
        uint netInt = ipInt & mask;

        var netIp = new IPAddress(new byte[]
        {
            (byte)(netInt >> 24),
            (byte)(netInt >> 16),
            (byte)(netInt >> 8),
            (byte)netInt
        });

        return $"{netIp}/{prefixLength}";
    }

    /// <summary>
    /// Generates all host IP addresses within the given CIDR prefix.
    /// Caps at maxHosts (default 512) to avoid runaway sweeps on wide prefixes.
    /// </summary>
    public static List<string> GetHostAddresses(string cidr, int maxHosts = 512)
    {
        var validation = IPv4Validator.ParseCidr(cidr);
        if (!validation.IsValid || validation.Address is null || !validation.PrefixLength.HasValue)
        {
            return new List<string>();
        }

        byte prefix = validation.PrefixLength.Value;
        if (!IPAddress.TryParse(validation.Address, out var ip))
        {
            return new List<string>();
        }

        byte[] ipBytes = ip.GetAddressBytes();
        if (ipBytes.Length != 4) return new List<string>();

        uint ipInt = ((uint)ipBytes[0] << 24) | ((uint)ipBytes[1] << 16) | ((uint)ipBytes[2] << 8) | (uint)ipBytes[3];
        uint mask = prefix == 0 ? 0 : 0xFFFFFFFF << (32 - prefix);
        uint netInt = ipInt & mask;
        uint bcastInt = netInt | ~mask;

        uint firstHost;
        uint lastHost;

        if (prefix >= 31)
        {
            firstHost = netInt;
            lastHost = bcastInt;
        }
        else
        {
            firstHost = netInt + 1;
            lastHost = bcastInt - 1;
        }

        if (firstHost > lastHost)
        {
            return new List<string> { validation.Address };
        }

        var hosts = new List<string>();
        for (uint cur = firstHost; cur <= lastHost && hosts.Count < maxHosts; cur++)
        {
            var hostIp = new IPAddress(new byte[]
            {
                (byte)(cur >> 24),
                (byte)(cur >> 16),
                (byte)(cur >> 8),
                (byte)cur
            });
            hosts.Add(hostIp.ToString());
        }

        return hosts;
    }
}
