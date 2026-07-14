using DigitalBrain.Kernel.Runtime;
using DigitalBrain.Integrations.Google.Contracts;
namespace DigitalBrain.Integrations.Google;

internal enum GmailLatestIncomingState
{
    SenderAvailable,
    EmptyInbox,
    SenderUnavailable,
    PositionUnavailable
}
internal sealed record GmailLatestIncomingMessage(GmailLatestIncomingState State, string? Sender = null, string? SenderAddress = null, string? MessageId = null, long? InternalDate = null);
internal sealed record GmailIncomingReadRequest(int Offset, string? AnchorMessageId = null, long? AnchorInternalDate = null);
internal enum GmailMailboxScope { Incoming, Inbox, Sent, Drafts, All }
internal enum GmailMessageReadState { Any, Read, Unread }
internal enum GmailAttachmentFilter { Any, HasAttachments, NoAttachments }
internal enum GmailMetadataReadState { Success, CapabilityUnavailable }
internal sealed record GmailMessageSelection(
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
internal sealed record GmailMessageListRequest(GmailMessageSelection Selection, int Offset = 0, int Limit = 10);
internal sealed record GmailThreadListRequest(GmailMessageSelection Selection, int Offset = 0, int Limit = 10, int MaxMessagesPerThread = 10);
internal sealed record GmailResultCoverage(
    int PagesRead,
    int CandidatesDiscovered,
    int MetadataRead,
    int MatchingMessages,
    int UnavailableMessages,
    bool ProviderExhausted,
    bool CandidateLimitReached);
internal sealed record GmailMessageMetadata(
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
internal sealed record GmailMessageListResult(
    GmailMetadataReadState State,
    GmailMessageMetadata[] Messages,
    GmailResultCoverage Coverage,
    string? SafeReason = null,
    string[]? StableCandidateMessageIds = null);
internal sealed record GmailMailboxOverview(int InboxMessages, int UnreadInboxMessages, int InboxThreads, int UnreadInboxThreads);
internal sealed record GmailThreadMetadata(
    string ThreadId,
    long LatestInternalDate,
    string? Subject,
    string[] ParticipantAddresses,
    bool HasUnread,
    int MatchingMessageCount,
    GmailMessageMetadata[] Messages);
internal sealed record GmailThreadListResult(
    GmailMetadataReadState State,
    GmailThreadMetadata[] Threads,
    GmailResultCoverage Coverage,
    string? SafeReason = null,
    string[]? StableCandidateMessageIds = null,
    string[]? StableCandidateThreadIds = null);
internal interface IGmailApiClient
{
    Task<GmailMessage> ReadMessageAsync(GmailMessageReadRequest request, CancellationToken cancellationToken = default) =>
        Task.FromException<GmailMessage>(new NotSupportedException());
    Task<GmailMailboxPage> ReadMailboxAsync(GmailMailboxReadRequest request, CancellationToken cancellationToken = default) =>
        Task.FromException<GmailMailboxPage>(new NotSupportedException());
    Task<GmailSendResult> SendAsync(GmailSendRequest request, CancellationToken cancellationToken = default);
    Task<GmailLatestIncomingMessage> ReadIncomingAtOffsetAsync(GmailIncomingReadRequest request, CancellationToken cancellationToken = default);
    Task<GmailMessageListResult> ListMessagesAsync(GmailMessageListRequest request, CancellationToken cancellationToken = default);
    Task<GmailMailboxOverview> ReadMailboxOverviewAsync(CancellationToken cancellationToken = default);
    Task<GmailThreadListResult> ListThreadsAsync(GmailThreadListRequest request, CancellationToken cancellationToken = default);
}
