namespace DigitalBrain.Abstractions.Repository;

[GenerateSerializer]
[Alias("db.start-repo-review")]
public sealed record StartRepoReview(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] string RootPath,
    [property: Id(2)] string Intent,
    [property: Id(3)] int MaxFiles = 30,
    [property: Id(4)] int ModeratorRounds = 3) : RequestSynapse<BehaviorRunStarted>;

