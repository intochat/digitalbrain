namespace DigitalBrain.Memory;

[GenerateSerializer, Alias("memory.page-state")]
internal sealed record MemoryPageState
{
    [Id(0)] public Dictionary<string, VectorMemoryEntry> Entries { get; init; } = [];
    [Id(1)] public Dictionary<string, MemoryDeletion> Deleted { get; init; } = [];
    [Id(2)] public HashSet<string> Pending { get; init; } = [];
}
