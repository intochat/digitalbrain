using Orleans.Concurrency;

namespace Brain.Runtime.Abstractions;

[GenerateSerializer, Immutable]
public sealed record RuntimeActivitySnapshot(
    [property: Id(0)] Guid Activity,
    [property: Id(1)] string OperationId,
    [property: Id(2)] string Workspace,
    [property: Id(3)] RuntimeActivityStatus Status,
    [property: Id(4)] long Sequence,
    [property: Id(5)] string? ResultJson,
    [property: Id(6)] string? Problem);
