namespace Etherprof.Contracts.Models;

public sealed record PublicIpInfo
{
    public string Ip { get; init; } = "";
    public string Asn { get; init; } = "";
    public string Org { get; init; } = "";

    public bool IsSuccess => !string.IsNullOrWhiteSpace(Ip);

    public string SummaryText
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Ip)) return "";
            if (string.IsNullOrWhiteSpace(Asn)) return Ip;
            if (string.IsNullOrWhiteSpace(Org)) return $"{Ip} · {Asn}";
            return $"{Ip} · {Asn} ({Org})";
        }
    }
}
