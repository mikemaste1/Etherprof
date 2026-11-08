using System.Text.Json;
using Etherprof.Contracts.Interfaces;
using Etherprof.Contracts.Models;

namespace Etherprof.Core;

public sealed class PublicIpService : IPublicIpService
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(5)
    };

    public PublicIpService()
    {
    }

    public async Task<PublicIpInfo?> ResolveAsync(CancellationToken cancellationToken = default)
    {
        // 1. Try ipapi.co/json/
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, "https://ipapi.co/json/");
            req.Headers.UserAgent.ParseAdd("Etherprof/1.0.1 (Network Diagnostics)");
            using var resp = await HttpClient.SendAsync(req, cancellationToken);
            if (resp.IsSuccessStatusCode)
            {
                var content = await resp.Content.ReadAsStringAsync(cancellationToken);
                var info = ParseIpApiCo(content);
                if (info != null && info.IsSuccess)
                {
                    return info;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to resolve public IP via ipapi.co: {ex.Message}");
        }

        // 2. Fallback to ipwho.is/
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, "https://ipwho.is/");
            req.Headers.UserAgent.ParseAdd("Etherprof/1.0.1 (Network Diagnostics)");
            using var resp = await HttpClient.SendAsync(req, cancellationToken);
            if (resp.IsSuccessStatusCode)
            {
                var content = await resp.Content.ReadAsStringAsync(cancellationToken);
                var info = ParseIpWhoIs(content);
                if (info != null && info.IsSuccess)
                {
                    return info;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to resolve public IP via ipwho.is: {ex.Message}");
        }

        return null;
    }

    public static PublicIpInfo? ParseIpApiCo(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("ip", out var ipEl)) return null;
            string ip = ipEl.GetString()?.Trim() ?? "";
            string asn = "";
            if (root.TryGetProperty("asn", out var asnEl))
            {
                asn = asnEl.GetString()?.Trim() ?? "";
            }
            string org = "";
            if (root.TryGetProperty("org", out var orgEl))
            {
                org = orgEl.GetString()?.Trim() ?? "";
            }
            return new PublicIpInfo
            {
                Ip = ip,
                Asn = NormalizeAsn(asn),
                Org = CleanOrg(org)
            };
        }
        catch
        {
            return null;
        }
    }

    public static PublicIpInfo? ParseIpWhoIs(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("success", out var succEl) && !succEl.GetBoolean())
                return null;

            if (!root.TryGetProperty("ip", out var ipEl)) return null;
            string ip = ipEl.GetString()?.Trim() ?? "";
            string asn = "";
            string org = "";

            if (root.TryGetProperty("connection", out var connEl))
            {
                if (connEl.TryGetProperty("asn", out var asnEl))
                {
                    if (asnEl.ValueKind == JsonValueKind.Number)
                        asn = asnEl.GetInt64().ToString();
                    else if (asnEl.ValueKind == JsonValueKind.String)
                        asn = asnEl.GetString()?.Trim() ?? "";
                }
                if (connEl.TryGetProperty("org", out var orgEl) && orgEl.ValueKind == JsonValueKind.String)
                {
                    org = orgEl.GetString()?.Trim() ?? "";
                }
                else if (connEl.TryGetProperty("isp", out var ispEl) && ispEl.ValueKind == JsonValueKind.String)
                {
                    org = ispEl.GetString()?.Trim() ?? "";
                }
            }

            return new PublicIpInfo
            {
                Ip = ip,
                Asn = NormalizeAsn(asn),
                Org = CleanOrg(org)
            };
        }
        catch
        {
            return null;
        }
    }

    private static string NormalizeAsn(string asn)
    {
        if (string.IsNullOrWhiteSpace(asn)) return "";
        asn = asn.Trim();
        if (asn.StartsWith("AS", StringComparison.OrdinalIgnoreCase))
            return asn.ToUpperInvariant();
        return $"AS{asn}";
    }

    private static string CleanOrg(string org)
    {
        if (string.IsNullOrWhiteSpace(org)) return "";
        org = org.Trim();
        if (org.Length >= 2 && ((org.StartsWith('"') && org.EndsWith('"')) || (org.StartsWith('\'') && org.EndsWith('\''))))
        {
            org = org[1..^1].Trim();
        }
        return org;
    }
}
