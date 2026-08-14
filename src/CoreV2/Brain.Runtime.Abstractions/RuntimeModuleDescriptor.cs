using Orleans.Concurrency;

namespace Brain.Runtime.Abstractions;

[GenerateSerializer, Immutable]
public sealed record RuntimeModuleDescriptor(
    [property: Id(0)] string Id,
    [property: Id(1)] string DisplayName,
    [property: Id(2)] RuntimeModuleStatus Status,
    [property: Id(3)] string? SetupMessage = null);
