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

public enum GmailMailboxScope { Incoming, Inbox, Sent, Drafts, All }
public enum GmailMessageReadState { Any, Read, Unread }
public enum GmailAttachmentFilter { Any, HasAttachments, NoAttachments }
public enum GmailMetadataReadState { Success, CapabilityUnavailable }

public sealed record GmailMessageSelection(
    GmailMailboxScope Mailbox = GmailMailboxScope.Incoming,
    GmailMessageReadState ReadState = GmailMessageReadState.Any,
    string? SenderAddress = null,
    string? RecipientAddress = null,
    string? SubjectContains = null,
    long? ReceivedAfterInclusive = null,
    long? ReceivedBeforeExclusive = null,
    GmailAttachmentFilter AttachmentFilter = GmailAttachmentFilter.Any,
    string[]? PinnedMessageIds = null,
    int MaxPages = 2,
    int MaxCandidates = 32);

public sealed record GmailMessageListRequest(GmailMessageSelection Selection, int Offset = 0, int Limit = 10);
public sealed record GmailThreadListRequest(
    GmailMessageSelection Selection,
    int Offset = 0,
    int Limit = 10,
    int MaxMessagesPerThread = 10);

public sealed record GmailResultCoverage(
    int PagesRead,
    int CandidatesDiscovered,
    int MetadataRead,
    int MatchingMessages,
    int UnavailableMessages,
    bool ProviderExhausted,
    bool CandidateLimitReached);

public sealed record GmailMessageMetadata(
    string MessageId,
    string? ThreadId,
    long InternalDate,
    string? From,
    string? FromAddress,
    string? To,
    string[] ToAddresses,
    string? Subject,
    string[] LabelIds,
    bool IsRead);

public sealed record GmailMessageListResult(
    GmailMetadataReadState State,
    GmailMessageMetadata[] Messages,
    GmailResultCoverage Coverage,
    string? SafeReason = null,
    string[]? StableCandidateMessageIds = null);

public sealed record GmailMailboxOverview(
    int InboxMessages,
    int UnreadInboxMessages,
    int InboxThreads,
    int UnreadInboxThreads);

public sealed record GmailThreadMetadata(
    string ThreadId,
    long LatestInternalDate,
    string? Subject,
    string[] ParticipantAddresses,
    bool HasUnread,
    int MatchingMessageCount,
    GmailMessageMetadata[] Messages);

public sealed record GmailThreadListResult(
    GmailMetadataReadState State,
    GmailThreadMetadata[] Threads,
    GmailResultCoverage Coverage,
    string? SafeReason = null,
    string[]? StableCandidateMessageIds = null,
    string[]? StableCandidateThreadIds = null);

public interface IGmailApiClient
{
    Task<GmailLatestIncomingMessage> ReadIncomingAtOffsetAsync(
        GmailIncomingReadRequest request,
        CancellationToken cancellationToken = default);

    Task<GmailMessageListResult> ListMessagesAsync(
        GmailMessageListRequest request,
        CancellationToken cancellationToken = default);

    Task<GmailMailboxOverview> ReadMailboxOverviewAsync(CancellationToken cancellationToken = default);

    Task<GmailThreadListResult> ListThreadsAsync(
        GmailThreadListRequest request,
        CancellationToken cancellationToken = default);
}
