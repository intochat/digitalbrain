namespace DigitalBrain.Conversations;

[GenerateSerializer]
[Alias("db.conversation.turn-snapshot")]
public sealed record ConversationTurnSnapshot(
    [property: Id(0)] TurnId TurnId,
    [property: Id(1)] CommandId CommandId,
    [property: Id(2)] string Text,
    [property: Id(3)] ConversationTurnStatus Status,
    [property: Id(4)] long Sequence,
    [property: Id(5)] string? ExecutionName = null);
