namespace DigitalBrain.Google;

public enum GmailLatestIncomingState
{
    SenderAvailable,
    EmptyInbox,
    SenderUnavailable,
    PositionUnavailable
}

public sealed record GmailLatestIncomingMessage(
    GmailLatestIncomingState State,
    string? Sender = null,
    string? SenderAddress = null,
    string? MessageId = null,
    long? InternalDate = null);

public sealed record GmailIncomingReadRequest(
    int Offset,
    string? AnchorMessageId = null,
    long? AnchorInternalDate = null);

public interface IGmailApiClient
{
    Task<GmailLatestIncomingMessage> ReadIncomingAtOffsetAsync(
        GmailIncomingReadRequest request,
        CancellationToken cancellationToken = default);
}
