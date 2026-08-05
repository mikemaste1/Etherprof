namespace Etherprof.Contracts.Models;

public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 1;
    public string? SelectedAdapterId { get; set; }
    public List<AdapterPreference> AdapterPreferences { get; set; } = new();
}
