using Orleans.Concurrency;

namespace Brain.Modules.Conversation.Contracts;

[GenerateSerializer, Immutable]
public sealed record ConversationMessage(
    [property: Id(0)] long Sequence,
    [property: Id(1)] string Role,
    [property: Id(2)] string Text,
    [property: Id(3)] string Principal);
