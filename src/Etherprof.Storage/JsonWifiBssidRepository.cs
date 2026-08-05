namespace Etherprof.Storage;

using Etherprof.Contracts.Interfaces;
using Etherprof.Contracts.Models;
using Microsoft.Extensions.Logging;

public sealed class JsonWifiBssidRepository : IWifiBssidRepository
{
    private readonly ILogger<JsonWifiBssidRepository> _logger;

    public JsonWifiBssidRepository(ILogger<JsonWifiBssidRepository> logger)
    {
        _logger = logger;
    }

    public async Task<IReadOnlyList<WifiBssidRecord>> LoadAsync()
    {
        try
        {
            var doc = await AtomicFileWriter.ReadAsync<WifiBssidsDocument>(StorageConstants.WifiBssidsFile);
            if (doc?.AccessPoints is not null)
            {
                _logger.LogInformation("Loaded {Count} BSSID records from {File}", doc.AccessPoints.Count, StorageConstants.WifiBssidsFile);
                return doc.AccessPoints;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load BSSID records from {File}", StorageConstants.WifiBssidsFile);
        }

        return Array.Empty<WifiBssidRecord>();
    }

    public async Task SaveAsync(IReadOnlyCollection<WifiBssidRecord> records)
    {
        try
        {
            var doc = new WifiBssidsDocument
            {
                SchemaVersion = StorageConstants.CurrentWifiBssidsSchemaVersion,
                AccessPoints = records.ToList()
            };
            await AtomicFileWriter.WriteAsync(StorageConstants.WifiBssidsFile, doc);
            _logger.LogDebug("Saved {Count} BSSID records to {File}", records.Count, StorageConstants.WifiBssidsFile);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save BSSID records to {File}", StorageConstants.WifiBssidsFile);
            throw;
        }
    }
}

public sealed class WifiBssidsDocument
{
    public int SchemaVersion { get; set; } = StorageConstants.CurrentWifiBssidsSchemaVersion;
    public List<WifiBssidRecord> AccessPoints { get; set; } = new();
}
