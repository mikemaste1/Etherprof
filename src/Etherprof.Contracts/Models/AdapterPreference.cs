namespace Etherprof.Contracts.Models;

public sealed class AdapterPreference
{
    public string AdapterId { get; set; } = "";
    public bool IsVisible { get; set; }
    public int SortOrder { get; set; }
}
