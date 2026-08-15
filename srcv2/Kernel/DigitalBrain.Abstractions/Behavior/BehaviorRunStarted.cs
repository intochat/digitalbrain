namespace DigitalBrain.Abstractions.Behavior;

[GenerateSerializer]
[Alias("db.behavior-run-started")]
public sealed record BehaviorRunStarted(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] BehaviorRunSummary Summary) : Synapse;

