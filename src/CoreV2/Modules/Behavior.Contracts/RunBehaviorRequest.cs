using Orleans.Concurrency;

namespace Brain.Modules.Behavior.Contracts;

[GenerateSerializer, Immutable]
public sealed record RunBehaviorRequest(
    [property: Id(0)] string RunId,
    [property: Id(1)] string Input,
    [property: Id(2)] string Principal,
    [property: Id(3)] string IdempotencyKey);
