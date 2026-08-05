namespace Etherprof.SpeedTest;

using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;

public static class SelectedAdapterHttpClientFactory
{
    public static HttpClient CreateClient(string localIpAddress, ILogger? logger = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(localIpAddress);

        if (!IPAddress.TryParse(localIpAddress, out var boundLocalIp))
        {
            throw new ArgumentException($"Invalid local IPv4 address '{localIpAddress}'.", nameof(localIpAddress));
        }

        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.None,
            PooledConnectionLifetime = TimeSpan.Zero,
            EnableMultipleHttp2Connections = false,
            ConnectCallback = async (context, cancellationToken) =>
            {
                logger?.LogDebug("Creating bound socket for host {Host}:{Port} on local IP {LocalIp}", context.DnsEndPoint.Host, context.DnsEndPoint.Port, boundLocalIp);

                // Perform DNS resolution (Technical Amendment 7)
                var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken);
                var targetIp = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);

                if (targetIp is null)
                {
                    throw new SocketException((int)SocketError.HostNotFound);
                }

                var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                try
                {
                    // Bind to local adapter IP (Technical Amendment 10/11)
                    socket.Bind(new IPEndPoint(boundLocalIp, 0));

                    // Connect to remote target
                    await socket.ConnectAsync(new IPEndPoint(targetIp, context.DnsEndPoint.Port), cancellationToken);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch (Exception ex)
                {
                    logger?.LogWarning(ex, "Failed to connect bound socket on local IP {LocalIp}", boundLocalIp);
                    socket.Dispose();
                    throw;
                }
            }
        };

        var client = new HttpClient(handler, disposeHandler: true);
        client.Timeout = TimeSpan.FromMinutes(3); // Generous timeout (Requirement 46)
        client.DefaultRequestHeaders.Add("User-Agent", "Etherprof/0.3");
        client.DefaultRequestHeaders.Add("Cache-Control", "no-cache");
        return client;
    }
}
