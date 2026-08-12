namespace DigitalBrain.Abstractions.Behavior;

[GenerateSerializer]
[Alias("db.read-behavior-run")]
public sealed record ReadBehaviorRun(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] string RunId) : RequestSynapse<BehaviorRunSnapshot>;

