using Orleans.Concurrency;

namespace Brain.Modules.Conversation.Contracts;

[GenerateSerializer, Immutable]
public sealed record ConversationSnapshot(
    [property: Id(0)] string ConversationId,
    [property: Id(1)] ConversationMessage[] Messages);
