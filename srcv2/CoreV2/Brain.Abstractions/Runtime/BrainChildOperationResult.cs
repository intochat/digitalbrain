using Orleans.Concurrency;

namespace Brain.Abstractions.Runtime;

[GenerateSerializer, Immutable]
public sealed record BrainChildOperationResult(
    [property: Id(0)] string OperationId,
    [property: Id(1)] string ResultJson);
