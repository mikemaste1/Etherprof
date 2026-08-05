namespace Etherprof.Core.Tests;

using Etherprof.Core;
using Xunit;

public class BssidNormalizerTests
{
    [Theory]
    [InlineData("84:16:f9:2a:11:03", "84:16:F9:2A:11:03")]
    [InlineData("84-16-F9-2A-11-03", "84:16:F9:2A:11:03")]
    [InlineData("8416F92A1103", "84:16:F9:2A:11:03")]
    [InlineData("8416f92a1103", "84:16:F9:2A:11:03")]
    [InlineData(" 84:16:F9:2A:11:03 ", "84:16:F9:2A:11:03")]
    public void Normalize_ValidBssids_ReturnsCanonicalFormat(string input, string expected)
    {
        var result = BssidNormalizer.Normalize(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("invalid")]
    [InlineData("84:16:F9:2A:11")]
    [InlineData("84:16:F9:2A:11:03:04")]
    [InlineData("84:16:F9:2A:11:ZZ")]
    public void Normalize_InvalidBssids_ThrowsArgumentException(string input)
    {
        Assert.Throws<ArgumentException>(() => BssidNormalizer.Normalize(input));
    }

    [Fact]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Behavior", "CA1806")]
    public void Normalize_Null_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => BssidNormalizer.Normalize(null!));
    }

    [Fact]
    public void TryNormalize_ValidAndInvalid_ReturnsExpected()
    {
        Assert.True(BssidNormalizer.TryNormalize("84-16-f9-2a-11-03", out var norm1));
        Assert.Equal("84:16:F9:2A:11:03", norm1);

        Assert.False(BssidNormalizer.TryNormalize("bad-mac", out var norm2));
        Assert.Equal("", norm2);
    }
}
