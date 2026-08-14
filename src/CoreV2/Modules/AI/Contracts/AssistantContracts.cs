using Orleans.Concurrency;

namespace Brain.Modules.AI.Contracts;

[GenerateSerializer, Immutable]
public sealed record AssistantChatInput([property: Id(0)] string Message);

[GenerateSerializer, Immutable]
public sealed record AssistantToolExecution(
    [property: Id(0)] string OperationId,
    [property: Id(1)] string ResultJson);

[GenerateSerializer, Immutable]
public sealed record AssistantChatResult(
    [property: Id(0)] string Response,
    [property: Id(1)] IReadOnlyList<AssistantToolExecution> Tools);
