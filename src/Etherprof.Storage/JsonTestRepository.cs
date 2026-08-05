namespace Etherprof.Storage;

using Etherprof.Contracts.Interfaces;
using Etherprof.Contracts.Models;
using Microsoft.Extensions.Logging;

public sealed class JsonTestRepository : ITestRepository
{
    private readonly ILogger<JsonTestRepository> _logger;

    public JsonTestRepository(ILogger<JsonTestRepository> logger)
    {
        _logger = logger;
    }

    public async Task<List<TestSet>> LoadAsync()
    {
        try
        {
            var wrapper = await AtomicFileWriter.ReadAsync<SchemaWrapper<List<TestSet>>>(StorageConstants.TestsFile);
            if (wrapper is not null)
            {
                _logger.LogInformation("Loaded {Count} test sets, schema version {Version}", wrapper.Data?.Count ?? 0, wrapper.SchemaVersion);
                return wrapper.Data ?? new();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load test sets, returning empty list");
        }

        return new();
    }

    public async Task SaveAsync(List<TestSet> testSets)
    {
        try
        {
            var wrapper = new SchemaWrapper<List<TestSet>>
            {
                SchemaVersion = StorageConstants.CurrentTestsSchemaVersion,
                Data = testSets
            };
            await AtomicFileWriter.WriteAsync(StorageConstants.TestsFile, wrapper);
            _logger.LogDebug("Saved {Count} test sets", testSets.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save test sets");
            throw;
        }
    }
}
