namespace Etherprof.Contracts.Models;

public sealed class WifiBssidRecord
{
    public string Bssid { get; init; } = "";

    public string? Alias { get; set; }
}
