namespace DigitalBrain.Conversations;

[GenerateSerializer]
[Alias("db.conversation.turn-status")]
public enum ConversationTurnStatus
{
    Queued = 0,
    Running = 1,
    Completed = 2,
    Cancelled = 3,
    Failed = 4,
}
