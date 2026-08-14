using Orleans.Concurrency;

namespace Brain.Modules.Memory.Contracts;

[GenerateSerializer, Immutable]
public sealed record StoreMemoryRequest(
    [property: Id(0)] string Key,
    [property: Id(1)] string Text,
    [property: Id(2)] string Principal,
    [property: Id(3)] string IdempotencyKey);
