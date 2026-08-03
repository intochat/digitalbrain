namespace DigitalBrain.Behaviors;

using DigitalBrain.Abstractions;

[GenerateSerializer]
[Alias("db.behavior.revision-proposed")]
public sealed record BehaviorRevisionProposed(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] BehaviorId Behavior,
    [property: Id(2)] string ArtifactHash) : Synapse;

[GenerateSerializer]
[Alias("db.behavior.compile-succeeded")]
public sealed record BehaviorCompileSucceeded(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] BehaviorId Behavior,
    [property: Id(2)] string ArtifactHash) : Synapse;

[GenerateSerializer]
[Alias("db.behavior.compile-failed")]
public sealed record BehaviorCompileFailed(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] BehaviorId Behavior,
    [property: Id(2)] string Diagnostics) : Synapse;

[GenerateSerializer]
[Alias("db.behavior.tests-passed")]
public sealed record BehaviorTestsPassed(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] BehaviorId Behavior,
    [property: Id(2)] string ArtifactHash,
    [property: Id(3)] int ScenarioCount) : Synapse;

[GenerateSerializer]
[Alias("db.behavior.tests-failed")]
public sealed record BehaviorTestsFailed(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] BehaviorId Behavior,
    [property: Id(2)] string ArtifactHash,
    [property: Id(3)] string Failure) : Synapse;

[GenerateSerializer]
[Alias("db.behavior.revision-approved")]
public sealed record BehaviorRevisionApproved(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] BehaviorId Behavior,
    [property: Id(2)] string ArtifactHash,
    [property: Id(3)] Guid ApprovalId) : Synapse;

[GenerateSerializer]
[Alias("db.behavior.approval-refused")]
public sealed record BehaviorRevisionApprovalRefused(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] BehaviorId Behavior,
    [property: Id(2)] string AttemptedFingerprint,
    [property: Id(3)] string Reason) : Synapse;

[GenerateSerializer]
[Alias("db.behavior.revision-activated")]
public sealed record BehaviorRevisionActivated(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] BehaviorId Behavior,
    [property: Id(2)] string ArtifactHash,
    [property: Id(3)] string? PriorArtifactHash) : Synapse;

[GenerateSerializer]
[Alias("db.behavior.revision-rolled-back")]
public sealed record BehaviorRevisionRolledBack(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] BehaviorId Behavior,
    [property: Id(2)] string RestoredArtifactHash,
    [property: Id(3)] string DemotedArtifactHash) : Synapse;

[GenerateSerializer]
[Alias("db.behavior.executed")]
public sealed record BehaviorExecuted(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] BehaviorId Behavior,
    [property: Id(2)] string ArtifactHash,
    [property: Id(3)] string Outcome) : Synapse;

[GenerateSerializer]
[Alias("db.behavior.revision-deployed")]
public sealed record BehaviorRevisionDeployed(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] BehaviorId Behavior,
    [property: Id(2)] string ArtifactHash) : Synapse;

[GenerateSerializer]
[Alias("db.behavior.revision-deploy-refused")]
public sealed record BehaviorRevisionDeployRefused(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] BehaviorId Behavior,
    [property: Id(2)] string ArtifactHash,
    [property: Id(3)] string Reason) : Synapse;

[GenerateSerializer]
[Alias("db.behavior.host-load-refused")]
public sealed record BehaviorHostLoadRefused(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] BehaviorId Behavior,
    [property: Id(2)] string ArtifactHash,
    [property: Id(3)] string Reason) : Synapse;

[GenerateSerializer]
[Alias("db.behavior.activation-gate-closed")]
public sealed record BehaviorActivationGateClosed(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] BehaviorId Behavior) : Synapse;

[GenerateSerializer]
[Alias("db.behavior.stopping")]
public sealed record BehaviorStopping(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] BehaviorId Behavior) : Synapse;

[GenerateSerializer]
[Alias("db.behavior.stopped")]
public sealed record BehaviorStopped(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] BehaviorId Behavior) : Synapse;

[GenerateSerializer]
[Alias("db.behavior.started")]
public sealed record BehaviorStarted(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] BehaviorId Behavior) : Synapse;

[GenerateSerializer]
[Alias("db.behavior.task-cancel-requested")]
public sealed record BehaviorTaskCancelRequested(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] BehaviorId Behavior,
    [property: Id(2)] NeuronId Task) : Synapse;

[GenerateSerializer]
[Alias("db.behavior.fact-emitted")]
public sealed record BehaviorFactEmitted(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] BehaviorId Behavior,
    [property: Id(2)] string ArtifactHash,
    [property: Id(3)] string EmitAlias) : Synapse;

[GenerateSerializer]
[Alias("db.behavior.fact-emit-refused")]
public sealed record BehaviorFactEmitRefused(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] BehaviorId Behavior,
    [property: Id(2)] string AttemptedAlias,
    [property: Id(3)] string Reason) : Synapse;
