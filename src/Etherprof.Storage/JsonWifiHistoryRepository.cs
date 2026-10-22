namespace Etherprof.Storage;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Etherprof.Contracts.Interfaces;
using Etherprof.Contracts.Models;
using Microsoft.Extensions.Logging;

public sealed class JsonWifiHistoryRepository : IWifiHistoryRepository
{
    private readonly ILogger<JsonWifiHistoryRepository> _logger;

    public JsonWifiHistoryRepository(ILogger<JsonWifiHistoryRepository> logger)
    {
        _logger = logger;
    }

    public async Task<IReadOnlyList<WifiNetworkHistoryRecord>> LoadAsync()
    {
        try
        {
            var doc = await AtomicFileWriter.ReadAsync<WifiHistoryDocument>(StorageConstants.WifiHistoryFile);
            if (doc?.Networks is not null && doc.Networks.Count > 0)
            {
                _logger.LogInformation("Loaded {Count} Wi-Fi history records from {File}", doc.Networks.Count, StorageConstants.WifiHistoryFile);
                return doc.Networks;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load Wi-Fi history records from {File}", StorageConstants.WifiHistoryFile);
        }

        // If no saved history file exists yet, seed from Windows profiles
        var seeded = await DiscoverSystemProfilesAsync();
        if (seeded.Count > 0)
        {
            try
            {
                await SaveAsync(seeded);
            }
            catch { }
        }

        return seeded;
    }

    public async Task SaveAsync(IReadOnlyCollection<WifiNetworkHistoryRecord> records)
    {
        try
        {
            var doc = new WifiHistoryDocument
            {
                SchemaVersion = StorageConstants.CurrentWifiHistorySchemaVersion,
                Networks = records.ToList()
            };
            await AtomicFileWriter.WriteAsync(StorageConstants.WifiHistoryFile, doc);
            _logger.LogDebug("Saved {Count} Wi-Fi history records to {File}", records.Count, StorageConstants.WifiHistoryFile);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save Wi-Fi history records to {File}", StorageConstants.WifiHistoryFile);
            throw;
        }
    }

    private static async Task<List<WifiNetworkHistoryRecord>> DiscoverSystemProfilesAsync()
    {
        return await Task.Run(() =>
        {
            var results = new List<WifiNetworkHistoryRecord>();
            try
            {
                var psi = new ProcessStartInfo("netsh", "wlan show profiles")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(psi);
                if (proc == null) return results;

                string output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(3000);

                var matches = Regex.Matches(output, @"(?i)(?:All User Profile|Профиль всех пользователей)\s*:\s*(.+)");
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (Match m in matches)
                {
                    string ssid = m.Groups[1].Value.Trim();
                    if (!string.IsNullOrEmpty(ssid) && seen.Add(ssid))
                    {
                        results.Add(new WifiNetworkHistoryRecord
                        {
                            Ssid = ssid,
                            FirstSeen = DateTimeOffset.UtcNow,
                            LastConnected = DateTimeOffset.UtcNow
                        });
                    }
                }
            }
            catch { }
            return results;
        });
    }
}

public sealed class WifiHistoryDocument
{
    public int SchemaVersion { get; set; } = StorageConstants.CurrentWifiHistorySchemaVersion;
    public List<WifiNetworkHistoryRecord> Networks { get; set; } = new();
}
