namespace DigitalBrain.Behaviors;

using DigitalBrain.Abstractions;

[GenerateSerializer]
[Alias("db.behavior.snapshot")]
public sealed record BehaviorSnapshot(
    [property: Id(0)] BehaviorId Behavior,
    [property: Id(1)] BehaviorRevisionStatus Status,
    [property: Id(2)] string? ProposedArtifactHash,
    [property: Id(3)] string? ActiveArtifactHash,
    [property: Id(4)] string? PriorArtifactHash,
    [property: Id(5)] string? LastCompileFailure,
    [property: Id(6)] bool TestsPassed,
    [property: Id(7)] bool IsApproved,
    [property: Id(8)] string? LastExecutionOutcome,
    [property: Id(9)] BehaviorRunState RunState = BehaviorRunState.Idle,
    [property: Id(10)] bool ActivationGateOpen = false,
    [property: Id(11)] string? DisplayName = null,
    [property: Id(12)] string? Description = null,
    [property: Id(13)] string? ProgramSource = null,
    [property: Id(14)] string? FeatureName = null,
    [property: Id(15)] string? FeatureText = null,
    [property: Id(16)] string? Overview = null,
    [property: Id(17)] string? ActiveSignatureHex = null,
    [property: Id(18)] int ActiveTaskCount = 0,
    [property: Id(19)] IReadOnlyList<BehaviorBindingSnapshot>? Bindings = null,
    [property: Id(20)] IReadOnlyList<BehaviorScenarioSnapshot>? Scenarios = null);

[GenerateSerializer]
[Alias("db.behavior.binding-snapshot")]
public sealed record BehaviorBindingSnapshot(
    [property: Id(0)] string BindingId,
    [property: Id(1)] string SourceModule,
    [property: Id(2)] string SourceSynapse,
    [property: Id(3)] string TargetCase,
    [property: Id(4)] string ContractVersion,
    [property: Id(5)] bool Enabled,
    [property: Id(6)] string ConfigurationHint);

[GenerateSerializer]
[Alias("db.behavior.scenario-snapshot")]
public sealed record BehaviorScenarioSnapshot(
    [property: Id(0)] string ScenarioId,
    [property: Id(1)] string Title,
    [property: Id(2)] string BindingKey,
    [property: Id(3)] bool? Passed,
    [property: Id(4)] string? Detail);

[GenerateSerializer]
[Alias("db.behavior.revision-status")]
public enum BehaviorRevisionStatus
{
    Empty = 0,
    Proposed = 1,
    CompileFailed = 2,
    TestsFailed = 3,
    TestsPassed = 4,
    Approved = 5,
    Active = 6,
}

[GenerateSerializer]
[Alias("db.behavior.run-state")]
public enum BehaviorRunState
{
    Idle = 0,
    Running = 1,
    Stopping = 2,
    Stopped = 3,
}
