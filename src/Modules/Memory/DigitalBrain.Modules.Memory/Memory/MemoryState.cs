namespace DigitalBrain.Memory;

[GenerateSerializer]
[Alias("memory.state")]
internal sealed record MemoryState(
    [property: Id(0)] int RememberedCount,
    [property: Id(1)] int ForgottenCount,
    [property: Id(2)] DateTimeOffset LastChangedAt)
{
    [Id(3)] public Dictionary<string, MemoryNamespace> Namespaces { get; init; } = [];
}

[GenerateSerializer, Alias("memory.namespace-state")]
internal sealed record MemoryNamespace
{
    [Id(0)] public int Generation { get; init; }
    [Id(1)] public HashSet<int> Pages { get; init; } = [];
    // Id(2) retired; never reuse
    [Id(3)] public bool IndexPurgePending { get; init; }
    [Id(4)] public Dictionary<int, HashSet<int>> RetiredPages { get; init; } = [];
}
