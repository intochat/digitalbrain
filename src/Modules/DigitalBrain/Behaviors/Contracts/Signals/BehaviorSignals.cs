using DigitalBrain.Contracts;

namespace DigitalBrain.Behavior;

[GenerateSerializer, Alias("behavior.deployment-changed")]
public sealed record BehaviorDeploymentChanged(
    [property: Id(0)] string ProgramId,
    [property: Id(1)] long Revision,
    [property: Id(2)] string ArtifactId) : Signal;

[GenerateSerializer, Alias("behavior.execution-changed")]
public sealed record BehaviorExecutionChanged(
    [property: Id(0)] string ProgramId,
    [property: Id(1)] Guid GenerationId,
    [property: Id(2)] BehaviorExecutionState State,
    [property: Id(3)] bool Ready,
    [property: Id(4)] string? Error) : Signal;

[GenerateSerializer, Alias("behavior.log-available")]
public sealed record BehaviorLogAvailable(
    [property: Id(0)] string ProgramId,
    [property: Id(1)] Guid GenerationId,
    [property: Id(2)] long LastSequence) : Signal;