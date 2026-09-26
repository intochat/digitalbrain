namespace DigitalBrain.Google.Gmail;

internal interface IGmailMailbox
{
    Task<GmailWatchReceipt> WatchAsync(string accessToken, string topicName, CancellationToken cancellationToken);
    Task<IReadOnlyList<GmailIncoming>> ListAddedAsync(string accessToken, string startHistoryId, CancellationToken cancellationToken);
}

internal sealed record GmailWatchReceipt(string HistoryId, DateTimeOffset Expiration);

internal sealed record GmailIncoming(string MessageId, string Subject);