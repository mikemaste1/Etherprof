using Etherprof.Core;
using Xunit;

namespace Etherprof.Core.Tests;

public class IPv4ValidatorTests
{
    [Theory]
    [InlineData("192.168.1.1", true)]
    [InlineData("10.0.0.1", true)]
    [InlineData("255.255.255.255", true)]
    [InlineData("0.0.0.0", true)]
    [InlineData("192.168.999.1", false)]
    [InlineData("192.168.1", false)]
    [InlineData("10.1", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("   ", false)]
    [InlineData("abc.def.ghi.jkl", false)]
    [InlineData("192.168.1.1.1", false)]
    public void IsValidAddress(string? address, bool expected)
    {
        Assert.Equal(expected, IPv4Validator.IsValidAddress(address));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(24, true)]
    [InlineData(32, true)]
    [InlineData(-1, false)]
    [InlineData(33, false)]
    public void IsValidPrefixLength(int prefix, bool expected)
    {
        Assert.Equal(expected, IPv4Validator.IsValidPrefixLength(prefix));
    }

    [Fact]
    public void ParseCidr_AddressOnly_DefaultsTo24()
    {
        var result = IPv4Validator.ParseCidr("192.168.1.50");
        Assert.True(result.IsValid);
        Assert.Equal("192.168.1.50", result.Address);
        Assert.Equal((byte)24, result.PrefixLength);
    }

    [Fact]
    public void ParseCidr_WithPrefix_UsesSpecifiedPrefix()
    {
        var result = IPv4Validator.ParseCidr("192.168.1.50/20");
        Assert.True(result.IsValid);
        Assert.Equal("192.168.1.50", result.Address);
        Assert.Equal((byte)20, result.PrefixLength);
    }

    [Fact]
    public void ParseCidr_PrefixZero_IsValid()
    {
        var result = IPv4Validator.ParseCidr("10.0.0.1/0");
        Assert.True(result.IsValid);
        Assert.Equal((byte)0, result.PrefixLength);
    }

    [Fact]
    public void ParseCidr_Prefix32_IsValid()
    {
        var result = IPv4Validator.ParseCidr("10.0.0.1/32");
        Assert.True(result.IsValid);
        Assert.Equal((byte)32, result.PrefixLength);
    }

    [Fact]
    public void ParseCidr_InvalidAddress_ReturnsError()
    {
        var result = IPv4Validator.ParseCidr("192.168.999.1");
        Assert.False(result.IsValid);
        Assert.Contains("Invalid IPv4", result.ErrorMessage);
    }

    [Fact]
    public void ParseCidr_InvalidPrefix_ReturnsError()
    {
        var result = IPv4Validator.ParseCidr("192.168.1.1/33");
        Assert.False(result.IsValid);
        Assert.Contains("0-32", result.ErrorMessage);
    }

    [Fact]
    public void ParseCidr_NonNumericPrefix_ReturnsError()
    {
        var result = IPv4Validator.ParseCidr("192.168.1.1/abc");
        Assert.False(result.IsValid);
        Assert.Contains("Invalid prefix", result.ErrorMessage);
    }

    [Fact]
    public void ParseCidr_EmptyInput_ReturnsError()
    {
        var result = IPv4Validator.ParseCidr("");
        Assert.False(result.IsValid);
    }

    [Fact]
    public void ParseCidr_WhitespaceHandled()
    {
        var result = IPv4Validator.ParseCidr("  192.168.1.50/24  ");
        Assert.True(result.IsValid);
        Assert.Equal("192.168.1.50", result.Address);
    }

    [Theory]
    [InlineData("192.168.1.1", true)]
    [InlineData("google.com", true)]
    [InlineData("my-server.local", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("-invalid", false)]
    [InlineData("invalid-", false)]
    public void IsValidHostOrAddress(string? host, bool expected)
    {
        Assert.Equal(expected, IPv4Validator.IsValidHostOrAddress(host));
    }
}
