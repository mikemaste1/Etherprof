namespace Etherprof.Core.Tests;

using Etherprof.Core;
using Xunit;

public class WifiQrCodeTests
{
    [Fact]
    public void Format_WpaPassword_FormatsCorrectly()
    {
        string qr = WifiQrFormatter.Format("MyOfficeNetwork", "SecretPass123");
        Assert.Equal("WIFI:S:MyOfficeNetwork;T:WPA;P:SecretPass123;;", qr);
    }

    [Fact]
    public void Format_SpecialCharacters_EscapesProperly()
    {
        string qr = WifiQrFormatter.Format("My;Office:\"Net\"", "Pass;123:\"abc\\xyz");
        Assert.Equal(@"WIFI:S:My\;Office\:\""Net\"";T:WPA;P:Pass\;123\:\""abc\\xyz;;", qr);
    }

    [Fact]
    public void Format_OpenNetwork_FormatsAsNoPass()
    {
        string qr = WifiQrFormatter.Format("FreePublicWifi", "");
        Assert.Equal("WIFI:S:FreePublicWifi;T:nopass;;", qr);

        string qrParenthesis = WifiQrFormatter.Format("FreePublicWifi", "(No security key configured / Open network)");
        Assert.Equal("WIFI:S:FreePublicWifi;T:nopass;;", qrParenthesis);
    }

    [Fact]
    public void Format_HiddenSsid_AddsHiddenFlag()
    {
        string qr = WifiQrFormatter.Format("HiddenNetwork", "Pass", isHidden: true);
        Assert.Equal("WIFI:S:HiddenNetwork;T:WPA;P:Pass;H:true;;", qr);
    }

    [Fact]
    public void QrCodeGenerator_GeneratesValidMatrix()
    {
        string content = "WIFI:S:TestNet;T:WPA;P:12345678;;";
        var matrix = QrCodeGenerator.GenerateMatrix(content);

        Assert.NotNull(matrix);
        int size = matrix.GetLength(0);
        // QR version 1 is 21x21, version 2 is 25x25, etc.
        Assert.True(size >= 21);
        Assert.Equal(size, matrix.GetLength(1));

        // Finder patterns in corners should be present:
        // Top-left finder pattern center (3, 3) must be true (dark)
        Assert.True(matrix[3, 3]);
        // Top-right finder pattern center (size - 4, 3) must be true (dark)
        Assert.True(matrix[3, size - 4]);
        // Bottom-left finder pattern center (3, size - 4) must be true (dark)
        Assert.True(matrix[size - 4, 3]);
    }
}
