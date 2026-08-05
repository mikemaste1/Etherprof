namespace Etherprof.Contracts.Models;

public enum NetworkProfileType
{
    Dhcp,
    Static,
    DnsOnly
}

public sealed class NetworkProfile
{
    public Guid Id { get; init; }
    public string Name { get; set; } = "";
    public NetworkProfileType Type { get; set; }
    public IPv4Configuration? IPv4 { get; set; }
    public bool ApplyDns { get; set; }
    public DnsConfiguration? Dns { get; set; }
    public int SortOrder { get; set; }
}
