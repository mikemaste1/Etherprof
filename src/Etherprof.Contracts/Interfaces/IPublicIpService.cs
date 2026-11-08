using Etherprof.Contracts.Models;

namespace Etherprof.Contracts.Interfaces;

public interface IPublicIpService
{
    Task<PublicIpInfo?> ResolveAsync(CancellationToken cancellationToken = default);
}
