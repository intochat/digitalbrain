using Orleans.Concurrency;

namespace Brain.Modules.Conversation.Contracts;

[GenerateSerializer, Immutable]
public sealed record ConversationAppendRequest(
    [property: Id(0)] string Principal,
    [property: Id(1)] string IdempotencyKey,
    [property: Id(2)] string Message);
