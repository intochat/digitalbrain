using Orleans;

namespace DigitalBrain.Kernel.V2;

public static class V2SalesforceTools
{
    public const string ReadLatestAccount = "salesforce.account.read.latest";
}

public enum V2SalesforceReadStatus
{
    Success,
    NeedsAuth,
    ConfigurationMissing,
    Unavailable
}

[GenerateSerializer, Alias("digitalbrain.v2.salesforce-read-result")]
public sealed record V2SalesforceReadResult(
    [property: Id(0)] V2SalesforceReadStatus Status,
    [property: Id(1)] string? Content = null,
    [property: Id(2)] string? SafeReason = null,
    [property: Id(3)] string? ConnectionUrl = null);

[Alias("digitalbrain.v2.salesforce-read-tool-grain")]
public interface IV2SalesforceReadToolGrain : IGrainWithStringKey
{
    [Alias("ReadLatestAccountAsync")]
    Task<V2SalesforceReadResult> ReadLatestAccountAsync(CancellationToken cancellationToken = default);
}
