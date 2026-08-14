using Orleans.Concurrency;

namespace Brain.Runtime.Abstractions;

[GenerateSerializer, Immutable]
public sealed record RuntimeInvocation(
    [property: Id(0)] string OperationId,
    [property: Id(1)] string InputJson,
    [property: Id(2)] string Workspace,
    [property: Id(3)] string Principal,
    [property: Id(4)] string IdempotencyKey);
