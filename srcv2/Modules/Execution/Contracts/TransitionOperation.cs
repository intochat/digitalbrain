using DigitalBrain.Abstractions;

namespace DigitalBrain.Execution;

[GenerateSerializer]
[Alias("db.execution.transition-operation")]
public sealed record TransitionOperation(
    [property: Id(0)] AttemptId Attempt,
    [property: Id(1)] string OperationKey,
    [property: Id(2)] OperationPhase ExpectedPhase,
    [property: Id(3)] OperationPhase Phase,
    [property: Id(4)] ProtectedPayloadReference? ResponsePayload,
    [property: Id(5)] string? RedactedSummary) : RequestSynapse<OperationSnapshot>;

