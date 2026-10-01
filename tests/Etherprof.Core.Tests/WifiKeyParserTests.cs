namespace Etherprof.Core.Tests;

using Etherprof.Core;
using Xunit;

public class WifiKeyParserTests
{
    [Fact]
    public void ExtractKey_EnglishNetshOutput_ExtractsKey()
    {
        string output = @"
Profile 1627.camb on interface Wi-Fi:
=======================================================================

Applied: All User Profile

Profile information
-------------------
    Version                : 1
    Type                   : Wireless LAN
    Name                   : 1627.camb
    Control options        : Connect automatically

Connectivity settings
---------------------
    Number of SSIDs        : 1
    SSID name              : ""1627.camb""
    Network type           : Infrastructure
    Radio type             : [ Any Radio Type ]

Security settings
-----------------
    Authentication         : WPA2-Personal
    Cipher                 : CCMP
    Authentication         : WPA2-Personal
    Cipher                 : CCMP
    Security key           : Present
    Key Content            : MySecureWifiPassword!123

Cost settings
-------------
    Cost                   : Unrestricted
";

        var key = WifiKeyParser.ExtractKeyFromNetshOutput(output);
        Assert.Equal("MySecureWifiPassword!123", key);
    }

    [Fact]
    public void ExtractKey_RussianNetshOutput_ExtractsKey()
    {
        string output = @"
Профиль 1627.camb в интерфейсе Беспроводная сеть:
=======================================================================

Применен: профиль всех пользователей

Сведения о профиле
-------------------
    Версия                 : 1
    Тип                    : Беспроводная сеть
    Имя                    : 1627.camb
    Параметры управления  : Подключаться автоматически

Параметры безопасности
-----------------
    Проверка подлинности   : WPA2-Personal
    Шифрование             : CCMP
    Ключ безопасности      : Присутствует
    Содержимое ключа       : tplinkrouter_pass

Параметры стоимости
-------------
    Стоимость              : Без ограничений
";

        var key = WifiKeyParser.ExtractKeyFromNetshOutput(output);
        Assert.Equal("tplinkrouter_pass", key);
    }

    [Fact]
    public void ExtractKey_OpenNetworkEnglish_ReturnsOpenNetworkMessage()
    {
        string output = @"
Profile FreeCoffee on interface Wi-Fi:
=======================================================================
Security settings
-----------------
    Authentication         : Open
    Cipher                 : None
    Security key           : Absent
";

        var key = WifiKeyParser.ExtractKeyFromNetshOutput(output);
        Assert.Equal(WifiKeyParser.OpenNetworkDisplay, key);
    }

    [Fact]
    public void ExtractKey_OpenNetworkRussian_ReturnsOpenNetworkMessage()
    {
        string output = @"
Профиль FreePublic in interface Wi-Fi:
=======================================================================
Параметры безопасности
-----------------
    Проверка подлинности   : Открытая
    Шифрование             : Нет
    Ключ безопасности      : Отсутствует
";

        var key = WifiKeyParser.ExtractKeyFromNetshOutput(output);
        Assert.Equal(WifiKeyParser.OpenNetworkDisplay, key);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Profile \"Unknown\" is not found on the system.")]
    public void ExtractKey_NullOrInvalidOutput_ReturnsNull(string? output)
    {
        var key = WifiKeyParser.ExtractKeyFromNetshOutput(output);
        Assert.Null(key);
    }
}
