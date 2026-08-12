namespace DigitalBrain.Conversations;

[GenerateSerializer]
[Alias("db.conversation.send-message")]
public sealed record SendConversationMessage(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] string Text,
    [property: Id(2)] ActorContext? Actor = null);
