namespace DigitalBrain.Chat;

[GenerateSerializer]
[Alias("chat.turn-status")]
public enum ChatTurnStatus
{
    Pending = 0,
    Running = 1,
    Completed = 2,
    Failed = 3,
    Cancelled = 4,
    Cancelling = 5,
    WaitingForUser = 6,
}
