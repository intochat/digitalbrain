using Orleans.Concurrency;

namespace Brain.Modules.Memory.Contracts;

[GenerateSerializer, Immutable]
public sealed record MemoryMutationResult(
    [property: Id(0)] string Namespace,
    [property: Id(1)] string Key,
    [property: Id(2)] string Status);
