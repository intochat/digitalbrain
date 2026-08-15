using Brain.Abstractions.Activities;
using Orleans.Concurrency;

namespace Brain.Abstractions.Runtime;

[GenerateSerializer, Immutable]
public sealed record BrainActivitySnapshot(
    [property: Id(0)] Guid ActivityId,
    [property: Id(1)] string OperationId,
    [property: Id(2)] string WorkspaceId,
    [property: Id(3)] ActivityStatus Status,
    [property: Id(4)] long Sequence,
    [property: Id(5)] string? ResultJson,
    [property: Id(6)] string? Problem);
