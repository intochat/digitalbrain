namespace DigitalBrain.Telegram;

public sealed record TelegramConnectionSnapshot(
    string State, string? BotUsername, string? PublicUrl, string? Error,
    DateTimeOffset? VerifiedAt, DateTimeOffset? LastMessageAt);

/// <summary>Operational state; provider receipts remain durable in IBot.</summary>
public sealed class TelegramConnectionStatus
{
    private TelegramConnectionSnapshot _snapshot = new("Starting", null, null, null, null, null);
    public string InstanceId { get; } = Guid.NewGuid().ToString("N");
    public TelegramConnectionSnapshot Read() => Volatile.Read(ref _snapshot);
    public void Update(string state, string? username, string? publicUrl, string? error, DateTimeOffset? verifiedAt = null)
    {
        TelegramConnectionSnapshot current;
        TelegramConnectionSnapshot next;
        do
        {
            current = Read();
            next = new(state, username, publicUrl, error, verifiedAt ?? current.VerifiedAt, current.LastMessageAt);
        } while (Interlocked.CompareExchange(ref _snapshot, next, current) != current);
    }
    public void MessageReceived()
    {
        TelegramConnectionSnapshot current;
        do
        {
            current = Read();
        } while (Interlocked.CompareExchange(ref _snapshot, current with { LastMessageAt = DateTimeOffset.UtcNow }, current) != current);
    }
}
