namespace DigitalBrain.Google;

public interface IGmailMcpClient
{
    Task<GmailMessageListResult> ListMessagesAsync(string query, int maxResults, CancellationToken cancellationToken = default);

    Task<GmailSendResult> SendMessageAsync(
        string to,
        string subject,
        string body,
        string idempotencyKey,
        CancellationToken cancellationToken = default);
}

public sealed record GmailMessageListResult(int MessageCount, string Summary);

public sealed record GmailSendResult(string ProviderMessageId);
