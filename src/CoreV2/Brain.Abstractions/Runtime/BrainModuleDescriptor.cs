using Orleans.Concurrency;

namespace Brain.Abstractions.Runtime;

[GenerateSerializer, Immutable]
public sealed record BrainModuleDescriptor(
    [property: Id(0)] string Id,
    [property: Id(1)] string DisplayName,
    [property: Id(2)] string Status,
    [property: Id(3)] string? SetupMessage = null);
