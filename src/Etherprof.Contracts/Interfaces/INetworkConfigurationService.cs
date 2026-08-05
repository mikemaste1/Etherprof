namespace Etherprof.Contracts.Interfaces;

using Etherprof.Contracts.Models;

public interface INetworkConfigurationService
{
    Task<ApplyResult> ApplyProfileAsync(
        string adapterId, NetworkProfile profile, CancellationToken ct = default);

    Task<ApplyResult> ApplyTemporaryAddressAsync(
        string adapterId, IPv4Configuration config, CancellationToken ct = default);

    Task<NetworkProfile> CaptureCurrentAsync(string adapterId);
}
