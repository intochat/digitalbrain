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
    [property: Id(10)] bool ActivationGateOpen = false);

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
