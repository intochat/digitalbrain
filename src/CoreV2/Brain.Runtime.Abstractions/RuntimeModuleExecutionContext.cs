using Orleans.Concurrency;

namespace Brain.Runtime.Abstractions;

[GenerateSerializer, Immutable]
public sealed record RuntimeModuleExecutionContext(
    [property: Id(0)] Guid Activity,
    [property: Id(1)] string Workspace,
    [property: Id(2)] string Principal,
    [property: Id(3)] string IdempotencyKey);
