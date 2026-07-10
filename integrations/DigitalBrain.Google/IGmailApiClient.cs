namespace DigitalBrain.Google;

public enum GmailLatestIncomingState
{
    SenderAvailable,
    EmptyInbox,
    SenderUnavailable
}

public sealed record GmailLatestIncomingMessage(
    GmailLatestIncomingState State,
    string? Sender = null,
    string? SenderAddress = null);

public interface IGmailApiClient
{
    Task<GmailLatestIncomingMessage> ReadLatestIncomingAsync(CancellationToken cancellationToken = default);
}
