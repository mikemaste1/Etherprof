namespace Etherprof.Contracts.Models;

public sealed class TestSet
{
    public Guid Id { get; init; }
    public string Name { get; set; } = "";
    public List<TestTarget> Targets { get; set; } = new();
    public int SortOrder { get; set; }
}
