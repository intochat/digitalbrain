using Orleans.Concurrency;

namespace Brain.Abstractions.Runtime;

[GenerateSerializer, Immutable]
public sealed record BrainActivityReceipt(
    [property: Id(0)] Guid ActivityId,
    [property: Id(1)] string OperationId);
