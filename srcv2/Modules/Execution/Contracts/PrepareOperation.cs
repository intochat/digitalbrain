using DigitalBrain.Abstractions;

namespace DigitalBrain.Execution;

[GenerateSerializer]
[Alias("db.execution.prepare-operation")]
public sealed record PrepareOperation(
    [property: Id(0)] AttemptId Attempt,
    [property: Id(1)] string OperationKey,
    [property: Id(2)] OperationEdge Edge,
    [property: Id(3)] ProtectedPayloadReference RequestPayload) : RequestSynapse<OperationSnapshot>;

