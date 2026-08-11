using DigitalBrain.Abstractions;

namespace DigitalBrain.Execution;

[GenerateSerializer]
[Alias("db.execution.operation-phase")]
public enum OperationPhase
{
    Prepared = 0,
    Dispatched = 1,
    Completed = 2,
    Uncertain = 3,
    Failed = 4,
}

[GenerateSerializer]
[Alias("db.execution.operation-edge")]
public sealed record OperationEdge(
    [property: Id(0)] NeuronId Target,
    [property: Id(1)] string RequestSynapseId,
    [property: Id(2)] int RequestSchemaVersion,
    [property: Id(3)] string ResponseSynapseId,
    [property: Id(4)] int ResponseSchemaVersion);

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

[GenerateSerializer]
[Alias("db.execution.prepare-operation")]
public sealed record PrepareOperation(
    [property: Id(0)] AttemptId Attempt,
    [property: Id(1)] string OperationKey,
    [property: Id(2)] OperationEdge Edge,
    [property: Id(3)] ProtectedPayloadReference RequestPayload) : RequestSynapse<OperationSnapshot>;

[GenerateSerializer]
[Alias("db.execution.transition-operation")]
public sealed record TransitionOperation(
    [property: Id(0)] AttemptId Attempt,
    [property: Id(1)] string OperationKey,
    [property: Id(2)] OperationPhase ExpectedPhase,
    [property: Id(3)] OperationPhase Phase,
    [property: Id(4)] ProtectedPayloadReference? ResponsePayload,
    [property: Id(5)] string? RedactedSummary) : RequestSynapse<OperationSnapshot>;

[GenerateSerializer]
[Alias("db.execution.read-operation-result")]
public sealed record ReadOperationResult(
    [property: Id(0)] OperationSnapshot? Operation) : Synapse;

[GenerateSerializer]
[Alias("db.execution.read-operation")]
public sealed record ReadOperation(
    [property: Id(0)] string OperationKey) : RequestSynapse<ReadOperationResult>;
