using Orleans.Concurrency;

namespace Brain.Modules.Behavior.Contracts;

[GenerateSerializer, Immutable]
public sealed record BehaviorRevision(
    [property: Id(0)] int Revision,
    [property: Id(1)] string Name,
    [property: Id(2)] string Source,
    [property: Id(3)] string Author);
