namespace DigitalBrain.Abstractions;

[GenerateSerializer]
[Alias("db.behavior-run-snapshot")]
public sealed record BehaviorRunSnapshot(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] BehaviorRunSummary Summary,
    [property: Id(2)] FileStance[] Stances,
    [property: Id(3)] ModeratorRound[] Rounds,
    [property: Id(4)] string Plan) : Synapse;

