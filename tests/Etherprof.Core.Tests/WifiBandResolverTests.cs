namespace Etherprof.Core.Tests;

using Etherprof.Contracts.Models;
using Etherprof.Core;
using Xunit;

public class WifiBandResolverTests
{
    [Theory]
    [InlineData(2412, null, WifiBand.GHz2_4)]
    [InlineData(2462, 11, WifiBand.GHz2_4)]
    [InlineData(5180, 36, WifiBand.GHz5)]
    [InlineData(5745, 149, WifiBand.GHz5)]
    [InlineData(5955, 1, WifiBand.GHz6)]
    [InlineData(6115, null, WifiBand.GHz6)]
    public void Resolve_Frequency_ReturnsCorrectBand(int frequencyMhz, int? channel, WifiBand expected)
    {
        var result = WifiBandResolver.Resolve(frequencyMhz, channel);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(null, 1, WifiBand.GHz2_4)]
    [InlineData(null, 6, WifiBand.GHz2_4)]
    [InlineData(null, 14, WifiBand.GHz2_4)]
    [InlineData(null, 36, WifiBand.GHz5)]
    [InlineData(null, 165, WifiBand.GHz5)]
    public void Resolve_ChannelOnly_ReturnsFallbackBand(int? frequencyMhz, int channel, WifiBand expected)
    {
        var result = WifiBandResolver.Resolve(frequencyMhz, channel);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Resolve_Unknown_ReturnsUnknown()
    {
        Assert.Equal(WifiBand.Unknown, WifiBandResolver.Resolve(null, null));
        Assert.Equal(WifiBand.Unknown, WifiBandResolver.Resolve(0, 0));
        Assert.Equal(WifiBand.Unknown, WifiBandResolver.Resolve(1000, 300));
    }
}
