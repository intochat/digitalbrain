namespace DigitalBrain.Conversations;

[GenerateSerializer]
[Alias("db.conversation.transcript")]
public sealed record ConversationTranscript(
    [property: Id(0)] IReadOnlyList<ConversationTurn> Turns,
    [property: Id(1)] long Watermark);
