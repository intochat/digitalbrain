using DigitalBrain.Contracts;

namespace DigitalBrain.AI.GroupChat.Signals;

[GenerateSerializer, Alias("ai.group-chat-changed")]
public sealed record GroupChatChanged([property: Id(0)] string ChatId, [property: Id(1)] long Revision, [property: Id(2)] GroupChatStatus Status) : Signal;

[GenerateSerializer, Alias("ai.group-chat-spoke")]
public sealed record GroupChatSpoke([property: Id(0)] string ChatId, [property: Id(1)] GroupChatTurn Turn) : Signal;

[GenerateSerializer, Alias("ai.group-chat-concluded")]
public sealed record GroupChatConcluded(
    [property: Id(0)] string ChatId,
    [property: Id(1)] string Answer,
    [property: Id(2)] int Rounds,
    [property: Id(3)] bool Agreed) : Signal;
