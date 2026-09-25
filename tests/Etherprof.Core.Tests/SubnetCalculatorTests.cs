namespace Etherprof.Core.Tests;

using Xunit;

public class SubnetCalculatorTests
{
    [Fact]
    public void GetNetworkCidr_ComputesCorrectSubnet()
    {
        var result = SubnetCalculator.GetNetworkCidr("192.168.1.45", 24);
        Assert.Equal("192.168.1.0/24", result);
    }

    [Fact]
    public void GetNetworkCidr_ComputesClassBSubnet()
    {
        var result = SubnetCalculator.GetNetworkCidr("172.16.55.10", 16);
        Assert.Equal("172.16.0.0/16", result);
    }

    [Fact]
    public void GetHostAddresses_Slash24_Returns254Hosts()
    {
        var hosts = SubnetCalculator.GetHostAddresses("192.168.1.0/24");
        Assert.Equal(254, hosts.Count);
        Assert.Equal("192.168.1.1", hosts[0]);
        Assert.Equal("192.168.1.254", hosts[^1]);
    }

    [Fact]
    public void GetHostAddresses_Slash30_Returns2Hosts()
    {
        var hosts = SubnetCalculator.GetHostAddresses("10.0.0.0/30");
        Assert.Equal(2, hosts.Count);
        Assert.Equal("10.0.0.1", hosts[0]);
        Assert.Equal("10.0.0.2", hosts[1]);
    }

    [Fact]
    public void GetHostAddresses_InvalidCidr_ReturnsEmpty()
    {
        var hosts = SubnetCalculator.GetHostAddresses("invalid");
        Assert.Empty(hosts);
    }
}
