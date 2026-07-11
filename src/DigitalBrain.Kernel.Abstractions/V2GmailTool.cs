using Orleans;

namespace DigitalBrain.Kernel.V2;

public static class V2GmailTools
{
    public const string ReadMessages = "gmail.read.messages";
    public const string ReadMailboxOverview = "gmail.read.mailbox-overview";
    public const string ReadThreads = "gmail.read.threads";
    public const string SummarizeThread = "gmail.read.content.thread-summary";
    public const string ReadIncomingAtOffset = "gmail.read.incoming-at-offset";
    public const string SummarizeIncoming = "gmail.read.summary";
    public const int MaximumResultCount = 10;
    public const int MaximumOffset = 4;
    public const int MaximumCandidateCount = 64;
    public const int MaximumPageCount = 4;
}

[GenerateSerializer, Alias("digitalbrain.v2.gmail-read-request")]
public sealed record V2GmailReadRequest(
    [property: Id(0)] int Offset,
    [property: Id(1)] string? AnchorMessageId = null,
    [property: Id(2)] long? AnchorInternalDate = null,
    [property: Id(3)] int TraversalDepth = 0,
    [property: Id(4)] bool RequiresAnchor = false);

public enum V2GmailReadStatus
{
    Success,
    NeedsAuth,
    ConfigurationMissing,
    Unavailable,
    CapabilityUnavailable
}

public enum V2GmailMailboxState
{
    SenderAvailable,
    EmptyInbox,
    SenderUnavailable,
    PositionUnavailable
}

[GenerateSerializer, Alias("digitalbrain.v2.gmail-read-result")]
public sealed record V2GmailReadResult(
    [property: Id(0)] V2GmailReadStatus Status,
    [property: Id(1)] string? Sender = null,
    [property: Id(2)] string? SafeReason = null,
    [property: Id(3)] string? ConnectionUrl = null,
    [property: Id(4)] string? SenderAddress = null,
    [property: Id(5)] V2GmailMailboxState MailboxState = V2GmailMailboxState.SenderAvailable,
    [property: Id(6)] string? MessageId = null,
    [property: Id(7)] long? InternalDate = null,
    [property: Id(8)] int TraversalDepth = 0,
    [property: Id(9)] bool AnchoredPrevious = false);

[Alias("digitalbrain.v2.gmail-read-tool-grain")]
public interface IV2GmailReadToolGrain : IGrainWithStringKey
{
    [Alias("ReadIncomingAtOffsetAsync")]
    Task<V2GmailReadResult> ReadIncomingAtOffsetAsync(
        V2GmailReadRequest request,
        CancellationToken cancellationToken = default);
}

public enum V2GmailMailboxScope
{
    Incoming,
    Inbox,
    Sent,
    Drafts,
    All
}

public enum V2GmailMessageReadState
{
    Any,
    Read,
    Unread
}

public enum V2GmailAttachmentFilter
{
    Any,
    HasAttachments,
    NoAttachments
}

[GenerateSerializer, Alias("digitalbrain.v2.gmail-message-selection")]
public sealed record V2GmailMessageSelection(
    [property: Id(0)] V2GmailMailboxScope Mailbox = V2GmailMailboxScope.Incoming,
    [property: Id(1)] V2GmailMessageReadState ReadState = V2GmailMessageReadState.Any,
    [property: Id(2)] string? SenderAddress = null,
    [property: Id(3)] string? RecipientAddress = null,
    [property: Id(4)] string? SubjectContains = null,
    [property: Id(5)] long? ReceivedAfterInclusive = null,
    [property: Id(6)] long? ReceivedBeforeExclusive = null,
    [property: Id(7)] V2GmailAttachmentFilter AttachmentFilter = V2GmailAttachmentFilter.Any,
    [property: Id(8)] string[]? PinnedMessageIds = null,
    [property: Id(9)] int MaxPages = 2,
    [property: Id(10)] int MaxCandidates = 32);

[GenerateSerializer, Alias("digitalbrain.v2.gmail-message-list-request")]
public sealed record V2GmailMessageListRequest(
    [property: Id(0)] V2GmailMessageSelection Selection,
    [property: Id(1)] int Offset = 0,
    [property: Id(2)] int Limit = V2GmailTools.MaximumResultCount);

[GenerateSerializer, Alias("digitalbrain.v2.gmail-thread-list-request")]
public sealed record V2GmailThreadListRequest(
    [property: Id(0)] V2GmailMessageSelection Selection,
    [property: Id(1)] int Offset = 0,
    [property: Id(2)] int Limit = V2GmailTools.MaximumResultCount,
    [property: Id(3)] int MaxMessagesPerThread = V2GmailTools.MaximumResultCount);

[GenerateSerializer, Alias("digitalbrain.v2.gmail-result-coverage")]
public sealed record V2GmailResultCoverage(
    [property: Id(0)] int PagesRead,
    [property: Id(1)] int CandidatesDiscovered,
    [property: Id(2)] int MetadataRead,
    [property: Id(3)] int MatchingMessages,
    [property: Id(4)] int UnavailableMessages,
    [property: Id(5)] bool ProviderExhausted,
    [property: Id(6)] bool CandidateLimitReached);

[GenerateSerializer, Alias("digitalbrain.v2.gmail-message-metadata")]
public sealed record V2GmailMessageMetadata(
    [property: Id(0)] string MessageId,
    [property: Id(1)] string? ThreadId,
    [property: Id(2)] long InternalDate,
    [property: Id(3)] string? From,
    [property: Id(4)] string? FromAddress,
    [property: Id(5)] string? To,
    [property: Id(6)] string[] ToAddresses,
    [property: Id(7)] string? Subject,
    [property: Id(8)] string[] LabelIds,
    [property: Id(9)] bool IsRead);

[GenerateSerializer, Alias("digitalbrain.v2.gmail-message-list-result")]
public sealed record V2GmailMessageListResult(
    [property: Id(0)] V2GmailReadStatus Status,
    [property: Id(1)] V2GmailMessageMetadata[] Messages,
    [property: Id(2)] V2GmailResultCoverage Coverage,
    [property: Id(3)] string? SafeReason = null,
    [property: Id(4)] string? ConnectionUrl = null,
    [property: Id(5)] string[]? StableCandidateMessageIds = null);

[GenerateSerializer, Alias("digitalbrain.v2.gmail-mailbox-overview-result")]
public sealed record V2GmailMailboxOverviewResult(
    [property: Id(0)] V2GmailReadStatus Status,
    [property: Id(1)] int InboxMessages = 0,
    [property: Id(2)] int UnreadInboxMessages = 0,
    [property: Id(3)] int InboxThreads = 0,
    [property: Id(4)] int UnreadInboxThreads = 0,
    [property: Id(5)] string? SafeReason = null,
    [property: Id(6)] string? ConnectionUrl = null);

[GenerateSerializer, Alias("digitalbrain.v2.gmail-thread-metadata")]
public sealed record V2GmailThreadMetadata(
    [property: Id(0)] string ThreadId,
    [property: Id(1)] long LatestInternalDate,
    [property: Id(2)] string? Subject,
    [property: Id(3)] string[] ParticipantAddresses,
    [property: Id(4)] bool HasUnread,
    [property: Id(5)] int MatchingMessageCount,
    [property: Id(6)] V2GmailMessageMetadata[] Messages);

[GenerateSerializer, Alias("digitalbrain.v2.gmail-thread-list-result")]
public sealed record V2GmailThreadListResult(
    [property: Id(0)] V2GmailReadStatus Status,
    [property: Id(1)] V2GmailThreadMetadata[] Threads,
    [property: Id(2)] V2GmailResultCoverage Coverage,
    [property: Id(3)] string? SafeReason = null,
    [property: Id(4)] string? ConnectionUrl = null,
    [property: Id(5)] string[]? StableCandidateMessageIds = null,
    [property: Id(6)] string[]? StableCandidateThreadIds = null);

[Alias("digitalbrain.v2.gmail-metadata-tool-grain")]
public interface IV2GmailMetadataToolGrain : IGrainWithStringKey
{
    [Alias("ReadMessagesAsync")]
    Task<V2GmailMessageListResult> ReadMessagesAsync(
        V2GmailMessageListRequest request,
        CancellationToken cancellationToken = default);

    [Alias("ReadMailboxOverviewAsync")]
    Task<V2GmailMailboxOverviewResult> ReadMailboxOverviewAsync(
        CancellationToken cancellationToken = default);

    [Alias("ReadThreadsAsync")]
    Task<V2GmailThreadListResult> ReadThreadsAsync(
        V2GmailThreadListRequest request,
        CancellationToken cancellationToken = default);
}
