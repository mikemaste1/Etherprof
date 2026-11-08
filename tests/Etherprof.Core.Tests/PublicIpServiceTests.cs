using Etherprof.Core;
using Xunit;

namespace Etherprof.Core.Tests;

public class PublicIpServiceTests
{
    [Fact]
    public void ParseIpApiCo_ValidJson_ReturnsPublicIpInfo()
    {
        string json = """
        {
          "ip": "93.177.248.128",
          "asn": "AS20910",
          "org": "SIA \"BITE Latvija\""
        }
        """;

        var result = PublicIpService.ParseIpApiCo(json);

        Assert.NotNull(result);
        Assert.Equal("93.177.248.128", result.Ip);
        Assert.Equal("AS20910", result.Asn);
        Assert.Equal("SIA \"BITE Latvija\"", result.Org);
        Assert.True(result.IsSuccess);
        Assert.Equal("93.177.248.128 · AS20910 (SIA \"BITE Latvija\")", result.SummaryText);
    }

    [Fact]
    public void ParseIpWhoIs_ValidJson_ReturnsPublicIpInfo()
    {
        string json = """
        {
          "ip": "1.1.1.1",
          "success": true,
          "connection": {
            "asn": 13335,
            "org": "Cloudflare, Inc."
          }
        }
        """;

        var result = PublicIpService.ParseIpWhoIs(json);

        Assert.NotNull(result);
        Assert.Equal("1.1.1.1", result.Ip);
        Assert.Equal("AS13335", result.Asn);
        Assert.Equal("Cloudflare, Inc.", result.Org);
        Assert.True(result.IsSuccess);
        Assert.Equal("1.1.1.1 · AS13335 (Cloudflare, Inc.)", result.SummaryText);
    }

    [Fact]
    public void ParseIpWhoIs_FailedResponse_ReturnsNull()
    {
        string json = """{"success": false, "message": "invalid IP"}""";
        var result = PublicIpService.ParseIpWhoIs(json);
        Assert.Null(result);
    }
}
