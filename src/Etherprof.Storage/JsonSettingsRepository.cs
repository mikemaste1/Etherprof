namespace Etherprof.Storage;

using Etherprof.Contracts.Interfaces;
using Etherprof.Contracts.Models;
using Microsoft.Extensions.Logging;

public sealed class JsonSettingsRepository : ISettingsRepository
{
    private readonly ILogger<JsonSettingsRepository> _logger;

    public JsonSettingsRepository(ILogger<JsonSettingsRepository> logger)
    {
        _logger = logger;
    }

    public async Task<AppSettings> LoadAsync()
    {
        try
        {
            var settings = await AtomicFileWriter.ReadAsync<AppSettings>(StorageConstants.SettingsFile);
            if (settings is not null)
            {
                _logger.LogInformation("Settings loaded, schema version {Version}", settings.SchemaVersion);
                return settings;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load settings, using defaults");
        }

        return new AppSettings();
    }

    public async Task SaveAsync(AppSettings settings)
    {
        try
        {
            settings.SchemaVersion = StorageConstants.CurrentSettingsSchemaVersion;
            await AtomicFileWriter.WriteAsync(StorageConstants.SettingsFile, settings);
            _logger.LogDebug("Settings saved");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save settings");
            throw;
        }
    }
}
