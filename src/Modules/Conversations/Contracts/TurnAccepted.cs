namespace DigitalBrain.Conversations;

[GenerateSerializer]
[Alias("db.conversation.turn-accepted")]
public sealed record TurnAccepted(
    [property: Id(0)] TurnId TurnId,
    [property: Id(1)] CommandId CommandId,
    [property: Id(2)] ConversationTurnStatus Status,
    [property: Id(3)] long Sequence);
