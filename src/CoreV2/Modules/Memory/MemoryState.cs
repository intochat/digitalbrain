using Brain.Modules.Memory.Contracts;

namespace Brain.Modules.Memory;

[GenerateSerializer]
public sealed class MemoryState
{
    [Id(0)]
    public string Namespace { get; set; } = string.Empty;

    [Id(1)]
    public Dictionary<string, MemoryRecord> Records { get; set; } = new(StringComparer.Ordinal);

    [Id(2)]
    public HashSet<string> ProcessedRequests { get; set; } = new(StringComparer.Ordinal);
}
