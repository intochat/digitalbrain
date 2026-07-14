using Orleans;

namespace DigitalBrain.Integrations.Google.Grains;

internal static class GmailTools
{
    public const string Send = "gmail.send";
    public const int MaximumResultCount = 100;
    public const int MaximumOffset = 10_000;
    public const int MaximumCandidateCount = 100;
    public const int MaximumPageCount = 4;
    public const int MaximumRecipientLength = 320;
    public const int MaximumSubjectLength = 998;
    public const int MaximumBodyLength = 100_000;
    public const int MaximumUniqueTagLength = 256;
}

internal enum GmailSendStatus
{
    Applied,
    AlreadyApplied,
    NeedsAuth,
    ConfigurationMissing,
    InvalidRequest,
    Unavailable
}

[GenerateSerializer, Alias("digitalbrain.v3.gmail-send-request")]
internal sealed record GmailSendRequest([property: Id(0)] string Recipient, [property: Id(1)] string Subject, [property: Id(2)] string Body, [property: Id(3)] string UniqueTag);

[GenerateSerializer, Alias("digitalbrain.v3.gmail-send-result")]
internal sealed record GmailSendResult(
    [property: Id(0)] GmailSendStatus Status,
    [property: Id(1)] string? MessageId = null,
    [property: Id(2)] string? ThreadId = null,
    [property: Id(3)] string? SafeReason = null);

[Alias("digitalbrain.v3.gmail-mutation-grain")]
internal interface IGmailMutationToolGrain : IGrainWithStringKey
{
    [Alias("SendAsync")]
    Task<GmailSendResult> SendAsync(GmailSendRequest request, CancellationToken cancellationToken = default);
}
