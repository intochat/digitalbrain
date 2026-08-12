namespace DigitalBrain.Abstractions;

[GenerateSerializer]
[Alias("db.start-repo-review")]
public sealed record StartRepoReview(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] string RootPath,
    [property: Id(2)] string Intent,
    [property: Id(3)] int MaxFiles = 30,
    [property: Id(4)] int ModeratorRounds = 3) : RequestSynapse<BehaviorRunStarted>;

[GenerateSerializer]
[Alias("db.behavior-run-started")]
public sealed record BehaviorRunStarted(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] BehaviorRunSummary Summary) : Synapse;

[GenerateSerializer]
[Alias("db.read-behavior-run")]
public sealed record ReadBehaviorRun(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] string RunId) : RequestSynapse<BehaviorRunSnapshot>;

[GenerateSerializer]
[Alias("db.behavior-run-snapshot")]
public sealed record BehaviorRunSnapshot(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] BehaviorRunSummary Summary,
    [property: Id(2)] FileStance[] Stances,
    [property: Id(3)] ModeratorRound[] Rounds,
    [property: Id(4)] string Plan) : Synapse;

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

[GenerateSerializer]
[Alias("db.file-stance")]
public sealed record FileStance(
    [property: Id(0)] string RelativePath,
    [property: Id(1)] string Stance,
    [property: Id(2)] string Rationale,
    [property: Id(3)] int Priority);

[GenerateSerializer]
[Alias("db.moderator-round")]
public sealed record ModeratorRound(
    [property: Id(0)] int Round,
    [property: Id(1)] string Summary,
    [property: Id(2)] string[] FocusPaths);
