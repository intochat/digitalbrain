using Orleans.Concurrency;

namespace Brain.Modules.Behavior.Contracts;

[GenerateSerializer, Immutable]
public sealed record BehaviorSnapshot(
    [property: Id(0)] string BehaviorId,
    [property: Id(1)] string Status,
    [property: Id(2)] int LatestRevision,
    [property: Id(3)] int? ActiveRevision,
    [property: Id(4)] BehaviorRevision[] Revisions,
    [property: Id(5)] BehaviorRun[] Runs);
