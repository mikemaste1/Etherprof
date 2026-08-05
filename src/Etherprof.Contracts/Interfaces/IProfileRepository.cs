namespace Etherprof.Contracts.Interfaces;

using Etherprof.Contracts.Models;

public interface IProfileRepository
{
    Task<List<NetworkProfile>> LoadAsync();
    Task SaveAsync(List<NetworkProfile> profiles);
}
