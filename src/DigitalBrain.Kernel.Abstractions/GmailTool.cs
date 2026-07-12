using Orleans;
using DigitalBrain.Kernel.Abstractions;
using DigitalBrain.Core.Runtime;

namespace DigitalBrain.Kernel.Runtime;

public static class GmailTools
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
public sealed record GmailReadRequest(
    [property: Id(0)] int Offset,
    [property: Id(1)] string? AnchorMessageId = null,
    [property: Id(2)] long? AnchorInternalDate = null,
    [property: Id(3)] int TraversalDepth = 0,
    [property: Id(4)] bool RequiresAnchor = false);

public enum GmailReadStatus
{
    Success,
    NeedsAuth,
    ConfigurationMissing,
    Unavailable,
    CapabilityUnavailable
}

public enum GmailMailboxState
{
    SenderAvailable,
    EmptyInbox,
    SenderUnavailable,
    PositionUnavailable
}

[GenerateSerializer, Alias("digitalbrain.v2.gmail-read-result")]
public sealed record GmailReadResult(
    [property: Id(0)] GmailReadStatus Status,
    [property: Id(1)] string? Sender = null,
    [property: Id(2)] string? SafeReason = null,
    [property: Id(3)] string? ConnectionUrl = null,
    [property: Id(4)] string? SenderAddress = null,
    [property: Id(5)] GmailMailboxState MailboxState = GmailMailboxState.SenderAvailable,
    [property: Id(6)] string? MessageId = null,
    [property: Id(7)] long? InternalDate = null,
    [property: Id(8)] int TraversalDepth = 0,
    [property: Id(9)] bool AnchoredPrevious = false);

[Alias("digitalbrain.v2.gmail-read-tool-grain")]
public interface IGmailReadToolGrain : IGrainWithStringKey
{
    [Alias("ReadIncomingAtOffsetAsync")]
    Task<GmailReadResult> ReadIncomingAtOffsetAsync(
        GmailReadRequest request,
        CancellationToken cancellationToken = default);

    [Alias("ResolveAuthorizationAsync")]
    Task<ExternalAuthorizationResolution> ResolveAuthorizationAsync(
        CancellationToken cancellationToken = default);

    [Alias("BeginAuthorizationAsync")]
    Task<GmailReadResult> BeginAuthorizationAsync(
        string flowReference,
        CancellationToken cancellationToken = default);

    [Alias("CompleteAuthorizationAsync")]
    Task<AuthResult> CompleteAuthorizationAsync(
        OAuthCallback callback,
        CancellationToken cancellationToken = default);
}

public enum GmailMailboxScope
{
    Incoming,
    Inbox,
    Sent,
    Drafts,
    All
}

public enum GmailMessageReadState
{
    Any,
    Read,
    Unread
}

public enum GmailAttachmentFilter
{
    Any,
    HasAttachments,
    NoAttachments
}

[GenerateSerializer, Alias("digitalbrain.v2.gmail-message-selection")]
public sealed record GmailMessageSelection(
    [property: Id(0)] GmailMailboxScope Mailbox = GmailMailboxScope.Incoming,
    [property: Id(1)] GmailMessageReadState ReadState = GmailMessageReadState.Any,
    [property: Id(2)] string? SenderAddress = null,
    [property: Id(3)] string? RecipientAddress = null,
    [property: Id(4)] string? SubjectContains = null,
    [property: Id(5)] long? ReceivedAfterInclusive = null,
    [property: Id(6)] long? ReceivedBeforeExclusive = null,
    [property: Id(7)] GmailAttachmentFilter AttachmentFilter = GmailAttachmentFilter.Any,
    [property: Id(8)] string[]? PinnedMessageIds = null,
    [property: Id(9)] int MaxPages = 2,
    [property: Id(10)] int MaxCandidates = 32);

[GenerateSerializer, Alias("digitalbrain.v2.gmail-message-list-request")]
public sealed record GmailMessageListRequest(
    [property: Id(0)] GmailMessageSelection Selection,
    [property: Id(1)] int Offset = 0,
    [property: Id(2)] int Limit = GmailTools.MaximumResultCount);

[GenerateSerializer, Alias("digitalbrain.v2.gmail-thread-list-request")]
public sealed record GmailThreadListRequest(
    [property: Id(0)] GmailMessageSelection Selection,
    [property: Id(1)] int Offset = 0,
    [property: Id(2)] int Limit = GmailTools.MaximumResultCount,
    [property: Id(3)] int MaxMessagesPerThread = GmailTools.MaximumResultCount);

[GenerateSerializer, Alias("digitalbrain.v2.gmail-result-coverage")]
public sealed record GmailResultCoverage(
    [property: Id(0)] int PagesRead,
    [property: Id(1)] int CandidatesDiscovered,
    [property: Id(2)] int MetadataRead,
    [property: Id(3)] int MatchingMessages,
    [property: Id(4)] int UnavailableMessages,
    [property: Id(5)] bool ProviderExhausted,
    [property: Id(6)] bool CandidateLimitReached);

[GenerateSerializer, Alias("digitalbrain.v2.gmail-message-metadata")]
public sealed record GmailMessageMetadata(
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
public sealed record GmailMessageListResult(
    [property: Id(0)] GmailReadStatus Status,
    [property: Id(1)] GmailMessageMetadata[] Messages,
    [property: Id(2)] GmailResultCoverage Coverage,
    [property: Id(3)] string? SafeReason = null,
    [property: Id(4)] string? ConnectionUrl = null,
    [property: Id(5)] string[]? StableCandidateMessageIds = null);

[GenerateSerializer, Alias("digitalbrain.v2.gmail-mailbox-overview-result")]
public sealed record GmailMailboxOverviewResult(
    [property: Id(0)] GmailReadStatus Status,
    [property: Id(1)] int InboxMessages = 0,
    [property: Id(2)] int UnreadInboxMessages = 0,
    [property: Id(3)] int InboxThreads = 0,
    [property: Id(4)] int UnreadInboxThreads = 0,
    [property: Id(5)] string? SafeReason = null,
    [property: Id(6)] string? ConnectionUrl = null);

[GenerateSerializer, Alias("digitalbrain.v2.gmail-thread-metadata")]
public sealed record GmailThreadMetadata(
    [property: Id(0)] string ThreadId,
    [property: Id(1)] long LatestInternalDate,
    [property: Id(2)] string? Subject,
    [property: Id(3)] string[] ParticipantAddresses,
    [property: Id(4)] bool HasUnread,
    [property: Id(5)] int MatchingMessageCount,
    [property: Id(6)] GmailMessageMetadata[] Messages);

[GenerateSerializer, Alias("digitalbrain.v2.gmail-thread-list-result")]
public sealed record GmailThreadListResult(
    [property: Id(0)] GmailReadStatus Status,
    [property: Id(1)] GmailThreadMetadata[] Threads,
    [property: Id(2)] GmailResultCoverage Coverage,
    [property: Id(3)] string? SafeReason = null,
    [property: Id(4)] string? ConnectionUrl = null,
    [property: Id(5)] string[]? StableCandidateMessageIds = null,
    [property: Id(6)] string[]? StableCandidateThreadIds = null);

[Alias("digitalbrain.v2.gmail-metadata-tool-grain")]
public interface IGmailMetadataToolGrain : IGrainWithStringKey
{
    [Alias("ReadMessagesAsync")]
    Task<GmailMessageListResult> ReadMessagesAsync(
        GmailMessageListRequest request,
        CancellationToken cancellationToken = default);

    [Alias("ReadMailboxOverviewAsync")]
    Task<GmailMailboxOverviewResult> ReadMailboxOverviewAsync(
        CancellationToken cancellationToken = default);

    [Alias("ReadThreadsAsync")]
    Task<GmailThreadListResult> ReadThreadsAsync(
        GmailThreadListRequest request,
        CancellationToken cancellationToken = default);
}
