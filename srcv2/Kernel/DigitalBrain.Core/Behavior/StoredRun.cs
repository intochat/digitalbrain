using DigitalBrain.Abstractions;

namespace DigitalBrain.Core;

[GenerateSerializer]
[Alias("db.behavior-stored-run")]
internal sealed record StoredRun(
    [property: Id(0)] BehaviorRunSummary Summary,
    [property: Id(1)] FileStance[] Stances,
    [property: Id(2)] ModeratorRound[] Rounds,
    [property: Id(3)] string Plan);
