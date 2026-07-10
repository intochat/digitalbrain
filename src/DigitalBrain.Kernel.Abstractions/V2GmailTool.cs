using Orleans;

namespace DigitalBrain.Kernel.V2;

public static class V2GmailTools
{
    public const string ReadLatest = "gmail.read.latest";
}

public enum V2GmailReadStatus
{
    Success,
    NeedsAuth,
    Unavailable
}

[GenerateSerializer, Alias("digitalbrain.v2.gmail-read-result")]
public sealed record V2GmailReadResult(
    [property: Id(0)] V2GmailReadStatus Status,
    [property: Id(1)] string? Content = null,
    [property: Id(2)] string? SafeReason = null,
    [property: Id(3)] string? ConnectionUrl = null);

[Alias("digitalbrain.v2.gmail-read-tool-grain")]
public interface IV2GmailReadToolGrain : IGrainWithStringKey
{
    [Alias("ReadLatestIncomingAsync")]
    Task<V2GmailReadResult> ReadLatestIncomingAsync(CancellationToken cancellationToken = default);
}
