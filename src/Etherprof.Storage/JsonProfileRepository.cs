namespace Etherprof.Storage;

using Etherprof.Contracts.Interfaces;
using Etherprof.Contracts.Models;
using Microsoft.Extensions.Logging;

public sealed class JsonProfileRepository : IProfileRepository
{
    private readonly ILogger<JsonProfileRepository> _logger;

    public JsonProfileRepository(ILogger<JsonProfileRepository> logger)
    {
        _logger = logger;
    }

    public async Task<List<NetworkProfile>> LoadAsync()
    {
        try
        {
            var wrapper = await AtomicFileWriter.ReadAsync<SchemaWrapper<List<NetworkProfile>>>(StorageConstants.ProfilesFile);
            if (wrapper is not null)
            {
                _logger.LogInformation("Loaded {Count} profiles, schema version {Version}", wrapper.Data?.Count ?? 0, wrapper.SchemaVersion);
                return wrapper.Data ?? new();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load profiles, returning empty list");
        }

        return new();
    }

    public async Task SaveAsync(List<NetworkProfile> profiles)
    {
        try
        {
            var wrapper = new SchemaWrapper<List<NetworkProfile>>
            {
                SchemaVersion = StorageConstants.CurrentProfilesSchemaVersion,
                Data = profiles
            };
            await AtomicFileWriter.WriteAsync(StorageConstants.ProfilesFile, wrapper);
            _logger.LogDebug("Saved {Count} profiles", profiles.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save profiles");
            throw;
        }
    }
}
