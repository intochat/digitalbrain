using Orleans.Concurrency;

namespace Brain.Modules.Behavior.Contracts;

[GenerateSerializer, Immutable]
public sealed record PublishBehaviorRequest(
    [property: Id(0)] string Name,
    [property: Id(1)] string Source,
    [property: Id(2)] string Principal,
    [property: Id(3)] string IdempotencyKey);
