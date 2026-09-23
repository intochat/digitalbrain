using DigitalBrain.Coding;

namespace DigitalBrain.Behavior;

[GenerateSerializer, Alias("behavior.deploy")]
public sealed record DeployBehavior(
    [property: Id(0)] long ExpectedRevision,
    [property: Id(1)] Guid OperationId,
    [property: Id(2)] CodeArtifactRef Artifact,
    [property: Id(3)] string ConfigurationJson,
    [property: Id(4)] Guid? AgentRunId = null);

[GenerateSerializer, Alias("behavior.change-state")]
public sealed record ChangeBehaviorState([property: Id(0)] long ExpectedRevision, [property: Id(1)] Guid OperationId);

[GenerateSerializer, Alias("behavior.rollback")]
public sealed record RollbackBehavior(
    [property: Id(0)] long ExpectedRevision,
    [property: Id(1)] Guid OperationId,
    [property: Id(2)] long DeploymentRevision);

[GenerateSerializer, Alias("behavior.delete")]
public sealed record DeleteBehavior([property: Id(0)] long ExpectedRevision, [property: Id(1)] Guid OperationId);

[Alias("behavior.desired-state")]
public enum BehaviorDesiredState { Stopped, Running }

[Alias("behavior.execution-state")]
public enum BehaviorExecutionState { Stopped, Starting, Running, Stopping, Completed, Failed, Interrupted }

[GenerateSerializer, Alias("behavior.deployment")]
public sealed record BehaviorDeployment(
    [property: Id(0)] long Revision,
    [property: Id(1)] CodeArtifactRef Artifact,
    [property: Id(2)] string ConfigurationJson,
    [property: Id(3)] DateTimeOffset CreatedAt,
    [property: Id(4)] Guid? AgentRunId);

[GenerateSerializer, Alias("behavior.snapshot")]
public sealed record BehaviorSnapshot(
    [property: Id(0)] long Revision,
    [property: Id(1)] BehaviorDesiredState DesiredState,
    [property: Id(2)] BehaviorExecutionState State,
    [property: Id(3)] long? DesiredDeploymentRevision,
    [property: Id(4)] long? ActiveDeploymentRevision,
    [property: Id(5)] Guid? GenerationId,
    [property: Id(6)] bool Ready,
    [property: Id(7)] string? Error,
    [property: Id(8)] IReadOnlyList<BehaviorDeployment> Deployments);

[GenerateSerializer, Alias("behavior.log-entry")]
public sealed record BehaviorLogEntry(
    [property: Id(0)] long Sequence,
    [property: Id(1)] Guid GenerationId,
    [property: Id(2)] DateTimeOffset At,
    [property: Id(3)] string Stream,
    [property: Id(4)] string Message);

[GenerateSerializer, Alias("behavior.log-page")]
public sealed record BehaviorLogPage(
    [property: Id(0)] IReadOnlyList<BehaviorLogEntry> Entries,
    [property: Id(1)] long LastSequence,
    [property: Id(2)] bool Truncated);