using Orleans.Concurrency;

namespace Brain.Abstractions.Runtime;

[GenerateSerializer, Immutable]
public sealed record BrainOperationDescriptor(
    [property: Id(0)] string Id,
    [property: Id(1)] string ModuleId,
    [property: Id(2)] string DisplayName,
    [property: Id(3)] string InputSchema,
    [property: Id(4)] string ResultSchema);
