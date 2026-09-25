namespace Etherprof.StreamTest.Tests;

using System.Net;
using System.Net.Sockets;
using Etherprof.StreamTest.Client;
using Etherprof.StreamTest.Models;
using Etherprof.StreamTest.Server;
using Xunit;

public class StreamClientServerIntegrationTests
{
    [Fact]
    public async Task Server_StartAndStop_LifecycleWorks()
    {
        await using var server = new StreamTestServer();
        Assert.False(server.IsRunning);

        await server.StartAsync(port: 0);
        Assert.True(server.IsRunning);
        Assert.True(server.BoundPort > 0);

        await server.StopAsync();
        Assert.False(server.IsRunning);
    }

    [Fact]
    public async Task TcpSendStream_LocalLoopback_Succeeds()
    {
        await using var server = new StreamTestServer();
        await server.StartAsync(0);
        int port = server.BoundPort;

        await using var client = new StreamTestClient();
        var request = new StreamTestRequest
        {
            ServerHost = "127.0.0.1",
            Port = port,
            Protocol = StreamTestProtocol.Tcp,
            Direction = StreamTestDirection.Send,
            TargetBitsPerSecond = 500_000,
            LocalIpAddress = "127.0.0.1"
        };

        await client.StartAsync(request);
        await Task.Delay(800);

        var stats = client.CurrentStatistics;
        Assert.True(client.IsRunning || client.State == StreamTestState.Active);
        Assert.True(stats.BytesTransferred > 0);

        await client.StopAsync();
        await server.StopAsync();
    }

    [Fact]
    public async Task UdpReceiveStream_LocalLoopback_Succeeds()
    {
        await using var server = new StreamTestServer();
        await server.StartAsync(0);
        int port = server.BoundPort;

        await using var client = new StreamTestClient();
        var request = new StreamTestRequest
        {
            ServerHost = "127.0.0.1",
            Port = port,
            Protocol = StreamTestProtocol.Udp,
            Direction = StreamTestDirection.Receive,
            TargetBitsPerSecond = 500_000,
            LocalIpAddress = "127.0.0.1"
        };

        await client.StartAsync(request);
        for (int i = 0; i < 20 && client.CurrentStatistics.BytesTransferred == 0; i++)
        {
            await Task.Delay(100);
        }

        var stats = client.CurrentStatistics;
        Assert.True(stats.BytesTransferred > 0);
        Assert.True(stats.PacketsReceived > 0);

        await client.StopAsync();
        await server.StopAsync();
    }
}
