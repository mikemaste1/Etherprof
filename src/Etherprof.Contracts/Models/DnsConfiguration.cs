namespace Etherprof.Contracts.Models;

public enum DnsMode
{
    Automatic,
    Static
}

public sealed class DnsConfiguration
{
    public DnsMode Mode { get; set; }
    public List<string> Servers { get; set; } = new();
}
