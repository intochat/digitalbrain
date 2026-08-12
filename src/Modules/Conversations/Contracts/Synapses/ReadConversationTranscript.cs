namespace DigitalBrain.Conversations;

[GenerateSerializer]
[Alias("db.conversation.read-transcript")]
public sealed record ReadConversationTranscript(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] long AfterSequence = 0,
    [property: Id(2)] int Limit = 64) : RequestSynapse<ConversationTranscript>;
