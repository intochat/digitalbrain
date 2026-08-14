using Orleans.Concurrency;

namespace Brain.Modules.Memory.Contracts;

[GenerateSerializer, Immutable]
public sealed record MemorySearchResult(
    [property: Id(0)] string Namespace,
    [property: Id(1)] MemoryRecord[] Records);
