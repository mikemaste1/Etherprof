namespace Etherprof.Core.Tests;

using Etherprof.Core;
using Xunit;

public class SpeedCalculationHelperTests
{
    [Theory]
    [InlineData(10_485_760L, 1.0, 83.88608)] // 10 MiB in 1 second = ~83.89 Mbps
    [InlineData(104_857_600L, 10.0, 83.88608)] // 100 MiB in 10 seconds = ~83.89 Mbps
    [InlineData(1_048_576L, 0.5, 16.777216)] // 1 MiB in 0.5 seconds = ~16.78 Mbps
    public void CalculateMbps_ValidInputs_ReturnsExpectedMbps(long bytes, double seconds, double expectedMbps)
    {
        var mbps = SpeedCalculationHelper.CalculateMbps(bytes, TimeSpan.FromSeconds(seconds));
        Assert.Equal(expectedMbps, mbps, precision: 4);
    }

    [Fact]
    public void CalculateMbps_ZeroOrNegative_ReturnsZero()
    {
        Assert.Equal(0.0, SpeedCalculationHelper.CalculateMbps(0, TimeSpan.FromSeconds(1)));
        Assert.Equal(0.0, SpeedCalculationHelper.CalculateMbps(1000, TimeSpan.Zero));
        Assert.Equal(0.0, SpeedCalculationHelper.CalculateMbps(-100, TimeSpan.FromSeconds(1)));
    }

    [Theory]
    [InlineData(1_048_576L, "1 MiB")]
    [InlineData(10_485_760L, "10 MiB")]
    [InlineData(104_857_600L, "100 MiB")]
    public void FormatSizeMiB_StandardSizes_ReturnsFormattedString(long bytes, string expected)
    {
        Assert.Equal(expected, SpeedCalculationHelper.FormatSizeMiB(bytes));
    }

    [Theory]
    [InlineData(8.754, "8.75 Mbps")]
    [InlineData(87.42, "87.4 Mbps")]
    [InlineData(150.0, "150.0 Mbps")]
    public void FormatMbps_ReturnsReadableString(double mbps, string expected)
    {
        Assert.Equal(expected, SpeedCalculationHelper.FormatMbps(mbps));
    }
}
