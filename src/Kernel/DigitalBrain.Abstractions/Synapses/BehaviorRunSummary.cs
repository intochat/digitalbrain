namespace DigitalBrain.Abstractions;

[GenerateSerializer]
[Alias("db.behavior-run-summary")]
public sealed record BehaviorRunSummary(
    [property: Id(0)] string RunId,
    [property: Id(1)] string Status,
    [property: Id(2)] string RootPath,
    [property: Id(3)] string Intent,
    [property: Id(4)] int FileCount,
    [property: Id(5)] int StanceCount,
    [property: Id(6)] int ModeratorRounds,
    [property: Id(7)] DateTimeOffset StartedAt,
    [property: Id(8)] DateTimeOffset? CompletedAt);

