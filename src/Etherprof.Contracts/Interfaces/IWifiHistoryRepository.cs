namespace Etherprof.Contracts.Interfaces;

using Etherprof.Contracts.Models;

public interface IWifiHistoryRepository
{
    Task<IReadOnlyList<WifiNetworkHistoryRecord>> LoadAsync();
    Task SaveAsync(IReadOnlyCollection<WifiNetworkHistoryRecord> records);
}
