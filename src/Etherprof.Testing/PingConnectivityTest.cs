namespace Etherprof.Testing;

using System.Net.NetworkInformation;
using Etherprof.Contracts.Interfaces;
using Etherprof.Contracts.Models;
using Microsoft.Extensions.Logging;

public sealed class PingConnectivityTest : IConnectivityTest
{
    private readonly ILogger<PingConnectivityTest> _logger;
    private const int TimeoutMs = 1000;

    public string Type => "ICMP";

    public PingConnectivityTest(ILogger<PingConnectivityTest> logger)
    {
        _logger = logger;
    }

    public async Task<TestResult> ExecuteAsync(TestTarget target, CancellationToken ct = default)
    {
        using var ping = new Ping();

        try
        {
            ct.ThrowIfCancellationRequested();

            var reply = await ping.SendPingAsync(target.Host, TimeoutMs);

            if (reply.Status == IPStatus.Success)
            {
                return new TestResult
                {
                    Timestamp = DateTimeOffset.Now,
                    Status = TestStatus.Success,
                    Latency = TimeSpan.FromMilliseconds(reply.RoundtripTime)
                };
            }
            else
            {
                return new TestResult
                {
                    Timestamp = DateTimeOffset.Now,
                    Status = TestStatus.Failure,
                    Error = reply.Status.ToString()
                };
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (PingException ex)
        {
            return new TestResult
            {
                Timestamp = DateTimeOffset.Now,
                Status = TestStatus.Failure,
                Error = ex.InnerException?.Message ?? ex.Message
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unexpected error pinging {Host}", target.Host);
            return new TestResult
            {
                Timestamp = DateTimeOffset.Now,
                Status = TestStatus.Failure,
                Error = ex.Message
            };
        }
    }
}
