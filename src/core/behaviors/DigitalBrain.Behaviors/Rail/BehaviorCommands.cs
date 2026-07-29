namespace DigitalBrain.Behaviors;

using DigitalBrain.Abstractions;

[GenerateSerializer]
[Alias("db.behavior.propose-revision")]
public sealed record ProposeBehaviorRevision(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] string ProgramSource,
    [property: Id(2)] IReadOnlyDictionary<string, string> Features,
    [property: Id(3)] string DisplayName,
    [property: Id(4)] string Description);

[GenerateSerializer]
[Alias("db.behavior.run-tests")]
public sealed record RunBehaviorTests(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] string ArtifactHash);

[GenerateSerializer]
[Alias("db.behavior.activate-revision")]
public sealed record ActivateBehaviorRevision(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] string ArtifactHash);

[GenerateSerializer]
[Alias("db.behavior.rollback-revision")]
public sealed record RollbackBehaviorRevision(
    [property: Id(0)] CommandId CommandId);

[GenerateSerializer]
[Alias("db.behavior.execute-revision")]
public sealed record ExecuteBehaviorRevision(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] string TriggerTypeName,
    [property: Id(2)] string TriggerJson);
