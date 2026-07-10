using Orleans;

namespace DigitalBrain.Kernel.V2;

public static class V2GmailTools
{
    public const string ReadIncomingAtOffset = "gmail.read.incoming-at-offset";
    public const string SummarizeIncoming = "gmail.read.summary";
    public const int MaximumOffset = 4;
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
    Unavailable
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
