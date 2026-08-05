namespace Etherprof.Contracts.Models;

public sealed class NetworkAdapter
{
    public string Id { get; init; } = "";           // NetworkInterface.Id (Windows GUID)
    public string Name { get; init; } = "";          // Friendly name
    public string Description { get; init; } = "";   // Driver description
    public string InterfaceType { get; init; } = "";  // Ethernet, Wireless80211, etc.
    public string MacAddress { get; init; } = "";
}
