using Orleans.Concurrency;

namespace Brain.Modules.Memory.Contracts;

[GenerateSerializer, Immutable]
public sealed record MemoryRecord(
    [property: Id(0)] string Key,
    [property: Id(1)] string Text,
    [property: Id(2)] string Principal);
