namespace Etherprof.Storage;

public sealed class SchemaWrapper<T>
{
    public int SchemaVersion { get; set; }
    public T Data { get; set; } = default!;
}
