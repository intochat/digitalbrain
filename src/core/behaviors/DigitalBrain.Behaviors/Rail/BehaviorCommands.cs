namespace DigitalBrain.Behaviors;

using DigitalBrain.Abstractions;
using DigitalBrain.Tasks;

[GenerateSerializer]
[Alias("db.behavior.propose-revision")]
public sealed record ProposeBehaviorRevision(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] string ProgramSource,
    [property: Id(2)] IReadOnlyDictionary<string, string> Features,
    [property: Id(3)] string DisplayName,
    [property: Id(4)] string Description);

[GenerateSerializer]
[Alias("db.behavior.run-tests")]
public sealed record RunBehaviorTests(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] string ArtifactHash);

[GenerateSerializer]
[Alias("db.behavior.activate-revision")]
public sealed record ActivateBehaviorRevision(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] string ArtifactHash);

[GenerateSerializer]
[Alias("db.behavior.activate-bound")]
public sealed record ActivateBoundBehavior(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] string ArtifactHash,
    [property: Id(2)] BehaviorActivationBinding Binding);

[GenerateSerializer]
[Alias("db.behavior.activation-goal")]
public sealed record BehaviorActivationGoal(
    [property: Id(0)] BehaviorId BehaviorId,
    [property: Id(1)] BehaviorRevisionId Revision,
    [property: Id(2)] string ContractVersion,
    [property: Id(3)] string CaseId,
    [property: Id(4)] ProtectedPayloadReference ProtectedPayload) : Goal;

[GenerateSerializer]
[Alias("db.behavior.bound-activation-result")]
public sealed record BoundBehaviorActivationResult(
    [property: Id(0)] NeuronId TaskId,
    [property: Id(1)] TaskState State,
    [property: Id(2)] AttemptId? ActiveAttempt,
    [property: Id(3)] BehaviorTaskActivation? Activation);

[GenerateSerializer]
[Alias("db.behavior.rollback-revision")]
public sealed record RollbackBehaviorRevision(
    [property: Id(0)] CommandId CommandId);

[GenerateSerializer]
[Alias("db.behavior.execute-revision")]
public sealed record ExecuteBehaviorRevision(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] string TriggerTypeName,
    [property: Id(2)] string TriggerJson);
