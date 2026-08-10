using DigitalBrain.Abstractions;

namespace DigitalBrain.Tasks;

[GenerateSerializer]
[Alias("tasks.operation-phase")]
public enum TaskOperationPhase
{
    Prepared = 0,
    Dispatched = 1,
    Completed = 2,
    Uncertain = 3,
}

[GenerateSerializer]
[Alias("tasks.operation-edge")]
public sealed record TaskOperationEdge(
    [property: Id(0)] NeuronId Target,
    [property: Id(1)] string RequestSynapseId,
    [property: Id(2)] int RequestSchemaVersion,
    [property: Id(3)] string ResponseSynapseId,
    [property: Id(4)] int ResponseSchemaVersion);

[GenerateSerializer]
[Alias("tasks.operation-snapshot")]
public sealed record TaskOperationSnapshot(
    [property: Id(0)] AttemptId Attempt,
    [property: Id(1)] int Sequence,
    [property: Id(2)] TaskOperationEdge Edge,
    [property: Id(3)] ProtectedPayloadReference RequestPayload,
    [property: Id(4)] TaskOperationPhase Phase,
    [property: Id(5)] ProtectedPayloadReference? ResponsePayload,
    [property: Id(6)] string? RedactedSummary) : Synapse;

[GenerateSerializer]
[Alias("tasks.prepare-operation")]
public sealed record PrepareTaskOperation(
    [property: Id(0)] AttemptId Attempt,
    [property: Id(1)] int Sequence,
    [property: Id(2)] TaskOperationEdge Edge,
    [property: Id(3)] ProtectedPayloadReference RequestPayload) : RequestSynapse<TaskOperationSnapshot>;

[GenerateSerializer]
[Alias("tasks.transition-operation")]
public sealed record TransitionTaskOperation(
    [property: Id(0)] AttemptId Attempt,
    [property: Id(1)] int Sequence,
    [property: Id(2)] TaskOperationPhase ExpectedPhase,
    [property: Id(3)] TaskOperationPhase Phase,
    [property: Id(4)] ProtectedPayloadReference? ResponsePayload,
    [property: Id(5)] string? RedactedSummary) : RequestSynapse<TaskOperationSnapshot>;

[GenerateSerializer]
[Alias("tasks.read-operation-result")]
public sealed record ReadTaskOperationResult(
    [property: Id(0)] TaskOperationSnapshot? Operation) : Synapse;

[GenerateSerializer]
[Alias("tasks.read-operation")]
public sealed record ReadTaskOperation(
    [property: Id(0)] AttemptId Attempt,
    [property: Id(1)] int Sequence) : RequestSynapse<ReadTaskOperationResult>;
