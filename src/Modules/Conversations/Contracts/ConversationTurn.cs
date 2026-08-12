namespace DigitalBrain.Conversations;

[GenerateSerializer]
[Alias("db.conversation.turn")]
public sealed record ConversationTurn(
    [property: Id(0)] TurnId TurnId,
    [property: Id(1)] CommandId CommandId,
    [property: Id(2)] string Role,
    [property: Id(3)] string Text,
    [property: Id(4)] long Sequence,
    [property: Id(5)] ConversationTurnStatus Status);
