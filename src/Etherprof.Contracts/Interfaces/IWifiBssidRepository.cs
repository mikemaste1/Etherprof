namespace Etherprof.Contracts.Interfaces;

using Etherprof.Contracts.Models;

public interface IWifiBssidRepository
{
    Task<IReadOnlyList<WifiBssidRecord>> LoadAsync();

    Task SaveAsync(IReadOnlyCollection<WifiBssidRecord> records);
}
