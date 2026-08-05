namespace Etherprof.StreamTest.Tests;

using Etherprof.StreamTest.Pacing;
using Xunit;

public class TokenBucketRateLimiterTests
{
    [Theory]
    [InlineData(200_000)]
    [InlineData(1_000_000)]
    [InlineData(10_000_000)]
    public void Constructor_ValidRates_Success(long rate)
    {
        var limiter = new TokenBucketRateLimiter(rate);
        Assert.Equal(rate, limiter.TargetBitsPerSecond);
    }

    [Fact]
    public async Task PaceAsync_ZeroOrNegativeBytes_ReturnsImmediately()
    {
        var limiter = new TokenBucketRateLimiter(2_000_000);
        using var cts = new CancellationTokenSource(1000);
        await limiter.PaceAsync(0, cts.Token);
        await limiter.PaceAsync(-100, cts.Token);
    }
}
