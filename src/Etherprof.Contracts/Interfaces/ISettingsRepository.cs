namespace Etherprof.Contracts.Interfaces;

using Etherprof.Contracts.Models;

public interface ISettingsRepository
{
    Task<AppSettings> LoadAsync();
    Task SaveAsync(AppSettings settings);
}
