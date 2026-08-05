namespace Etherprof.Contracts.Models;

public sealed class TestTarget
{
    public Guid Id { get; init; }
    public string Name { get; set; } = "";
    public string Host { get; set; } = "";
}
