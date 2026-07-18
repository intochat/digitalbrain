using Brain.Contracts;
using Brain.Kernel;

namespace DigitalBrain.Salesforce;

[Alias("digitalbrain.salesforce.ISalesforceNeuronControl")]
public interface ISalesforceNeuronControl : IGrainWithStringKey
{
    [Alias("SetAutoDrainAsync")]
    Task SetAutoDrainAsync(bool enabled);

    [Alias("DrainOutboxAsync")]
    Task DrainOutboxAsync();

    [Alias("DrainOutboxStrictAsync")]
    Task DrainOutboxStrictAsync();

    [Alias("GetOutboxCountAsync")]
    Task<int> GetOutboxCountAsync();

    [Alias("PeekOutboxAsync")]
    Task<OutboxIntent<SalesforceFeedEvent>?> PeekOutboxAsync();

    [Alias("ReplayOutboxIntentAsync")]
    Task ReplayOutboxIntentAsync(OutboxIntent<SalesforceFeedEvent> intent);

    [Alias("GetTelemetryAsync")]
    Task<IReadOnlyList<string>> GetTelemetryAsync();

    [Alias("GetLastFailureAsync")]
    Task<SanitizedFailure?> GetLastFailureAsync();

    [Alias("GetActivationTokenAsync")]
    Task<Guid> GetActivationTokenAsync();

    [Alias("RequestDeactivationAsync")]
    Task RequestDeactivationAsync();

    [Alias("GetProviderUpdateCallsAsync")]
    Task<int> GetProviderUpdateCallsAsync();
}
