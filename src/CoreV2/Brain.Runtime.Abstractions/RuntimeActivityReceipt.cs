using Orleans.Concurrency;

namespace Brain.Runtime.Abstractions;

[GenerateSerializer, Immutable]
public sealed record RuntimeActivityReceipt(
    [property: Id(0)] Guid Activity,
    [property: Id(1)] string OperationId);
