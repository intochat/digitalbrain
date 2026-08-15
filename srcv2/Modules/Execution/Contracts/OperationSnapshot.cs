using DigitalBrain.Abstractions;

namespace DigitalBrain.Execution;

[GenerateSerializer]
[Alias("db.execution.operation-snapshot")]
public sealed record OperationSnapshot(
    [property: Id(0)] string OperationKey,
    [property: Id(1)] AttemptId Attempt,
    [property: Id(2)] OperationEdge Edge,
    [property: Id(3)] ProtectedPayloadReference RequestPayload,
    [property: Id(4)] OperationPhase Phase,
    [property: Id(5)] ProtectedPayloadReference? ResponsePayload,
    [property: Id(6)] string? RedactedSummary) : Synapse;

