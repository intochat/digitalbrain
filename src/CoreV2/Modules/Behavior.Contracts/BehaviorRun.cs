using Orleans.Concurrency;

namespace Brain.Modules.Behavior.Contracts;

[GenerateSerializer, Immutable]
public sealed record BehaviorRun(
    [property: Id(0)] string RunId,
    [property: Id(1)] int Revision,
    [property: Id(2)] string Input,
    [property: Id(3)] string Output,
    [property: Id(4)] string Principal);
