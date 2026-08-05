namespace Etherprof.Contracts.Models;

public sealed class NetworkAdapterState
{
    public string AdapterId { get; init; } = "";
    public bool IsAvailable { get; init; }
    public bool IsConnected { get; init; }
    public bool IsDhcpEnabled { get; init; }        // From Get-NetIPInterface (authoritative)
    public bool IsDnsAutomatic { get; init; }       // From registry NameServer check
    public string? IPv4Address { get; init; }
    public byte? PrefixLength { get; init; }
    public string? Gateway { get; init; }
    public IReadOnlyList<string> DnsServers { get; init; } = [];
    public string? LinkSpeed { get; init; }
}
