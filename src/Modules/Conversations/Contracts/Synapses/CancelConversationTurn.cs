namespace DigitalBrain.Conversations;

[GenerateSerializer]
[Alias("db.conversation.cancel-turn")]
public sealed record CancelConversationTurn(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] TurnId TurnId,
    [property: Id(2)] ActorContext? Actor = null,
    [property: Id(3)] long? ExpectedRevision = null);
