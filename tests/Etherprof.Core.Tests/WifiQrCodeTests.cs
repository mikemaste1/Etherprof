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

    [Fact]
    public void Parse_StandardWpaWifiString_ParsesSuccessfully()
    {
        string qr = "WIFI:S:MyNetwork;T:WPA;P:SecretPassword123;H:false;;";
        bool success = WifiQrParser.TryParse(qr, out var info);

        Assert.True(success);
        Assert.NotNull(info);
        Assert.Equal("MyNetwork", info.Ssid);
        Assert.Equal("SecretPassword123", info.Password);
        Assert.Equal("WPA", info.AuthType);
        Assert.False(info.IsHidden);
    }

    [Fact]
    public void Parse_EscapedCharacters_UnescapesProperly()
    {
        string qr = @"WIFI:S:My\;Office\:\""Net\"";T:WPA;P:Pass\;123\:\""abc\\xyz;;";
        bool success = WifiQrParser.TryParse(qr, out var info);

        Assert.True(success);
        Assert.NotNull(info);
        Assert.Equal("My;Office:\"Net\"", info.Ssid);
        Assert.Equal("Pass;123:\"abc\\xyz", info.Password);
    }

    [Fact]
    public void Parse_OpenNetworkNoPass_ParsesSuccessfully()
    {
        string qr = "WIFI:S:PublicWifi;T:nopass;;";
        bool success = WifiQrParser.TryParse(qr, out var info);

        Assert.True(success);
        Assert.NotNull(info);
        Assert.Equal("PublicWifi", info.Ssid);
        Assert.Null(info.Password);
        Assert.Equal("nopass", info.AuthType);
        Assert.False(info.IsHidden);
    }

    [Fact]
    public void Parse_DifferentFieldOrder_ParsesSuccessfully()
    {
        string qr = "WIFI:T:WPA2;P:Secret123;H:true;S:GuestWifi;;";
        bool success = WifiQrParser.TryParse(qr, out var info);

        Assert.True(success);
        Assert.NotNull(info);
        Assert.Equal("GuestWifi", info.Ssid);
        Assert.Equal("Secret123", info.Password);
        Assert.Equal("WPA2", info.AuthType);
        Assert.True(info.IsHidden);
    }

    [Fact]
    public void Parse_InvalidOrNonWifiString_ReturnsFalse()
    {
        Assert.False(WifiQrParser.TryParse("https://google.com", out _));
        Assert.False(WifiQrParser.TryParse("", out _));
        Assert.False(WifiQrParser.TryParse(null, out _));
        Assert.False(WifiQrParser.TryParse("WIFI:T:WPA;;", out _)); // missing SSID
    }
}
