namespace DigitalBrain.Behaviors;

using DigitalBrain.Abstractions;

[GenerateSerializer]
[Alias("db.behavior.execution-result")]
public sealed record BehaviorExecutionResult(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] BehaviorId Behavior,
    [property: Id(2)] string ArtifactHash,
    [property: Id(3)] string Outcome,
    [property: Id(4)] bool Succeeded);
