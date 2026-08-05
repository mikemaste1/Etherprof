namespace Etherprof.Contracts.Models;

public sealed class IPv4Configuration
{
    public string Address { get; set; } = "";
    public byte PrefixLength { get; set; }
    public string? Gateway { get; set; }
}
