using Orleans.Concurrency;

namespace Brain.Modules.UI.Contracts;

[GenerateSerializer, Immutable]
public sealed record ChatSendInput([property: Id(0)] string Message);

[GenerateSerializer, Immutable]
public sealed record ChatToolResult(
    [property: Id(0)] string OperationId,
    [property: Id(1)] string ResultJson);

[GenerateSerializer, Immutable]
public sealed record ChatTurnResult(
    [property: Id(0)] string Response,
    [property: Id(1)] IReadOnlyList<ChatToolResult> Tools);

[GenerateSerializer, Immutable]
public sealed record ChatTurnEnvelope(
    [property: Id(0)] Guid ActivityId,
    [property: Id(1)] ChatTurnResult Turn);
