namespace Etherprof.Network.Windows;

using System.Net;
using System.Net.Sockets;

public static class SelectedAdapterSocketFactory
{
    public static IPAddress ParseOrResolveLocalIPv4(string localIpAddress)
    {
        if (string.IsNullOrWhiteSpace(localIpAddress) || !IPAddress.TryParse(localIpAddress, out var ip) || ip.AddressFamily != AddressFamily.InterNetwork)
        {
            throw new InvalidOperationException("Cannot start stream test. Selected adapter has no usable IPv4 address.");
        }
        return ip;
    }

    public static async Task<IPAddress> ResolveTargetIPv4Async(string host, CancellationToken cancellationToken = default)
    {
        if (IPAddress.TryParse(host, out var parsedIp))
        {
            if (parsedIp.AddressFamily == AddressFamily.InterNetwork)
            {
                return parsedIp;
            }
            throw new InvalidOperationException($"Host {host} is IPv6, but IPv4 is required for bound adapter stream tests.");
        }

        var addresses = await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);
        var targetIp = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
        if (targetIp == null)
        {
            throw new SocketException((int)SocketError.HostNotFound);
        }
        return targetIp;
    }

    public static Socket CreateBoundTcpSocket(IPAddress localIp)
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket.Bind(new IPEndPoint(localIp, 0));
        return socket;
    }

    public static Socket CreateBoundUdpSocket(IPAddress localIp)
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.Bind(new IPEndPoint(localIp, 0));
        return socket;
    }
}
