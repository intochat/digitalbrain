namespace DigitalBrain.Conversations;

[GenerateSerializer]
[Alias("db.conversation.note")]
public sealed record ConversationNote([property: Id(0)] string Text) : Synapse;
