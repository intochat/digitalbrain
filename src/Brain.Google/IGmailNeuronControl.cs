using Brain.Contracts;
using Brain.Kernel;

namespace DigitalBrain.Google;

[Alias("digitalbrain.google.IGmailNeuronControl")]
public interface IGmailNeuronControl : IGrainWithStringKey
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
    Task<OutboxIntent<GmailFeedEvent>?> PeekOutboxAsync();

    [Alias("ReplayOutboxIntentAsync")]
    Task ReplayOutboxIntentAsync(OutboxIntent<GmailFeedEvent> intent);

    [Alias("GetTelemetryAsync")]
    Task<IReadOnlyList<string>> GetTelemetryAsync();

    [Alias("GetLastFailureAsync")]
    Task<SanitizedFailure?> GetLastFailureAsync();

    [Alias("GetActivationTokenAsync")]
    Task<Guid> GetActivationTokenAsync();

    [Alias("RequestDeactivationAsync")]
    Task RequestDeactivationAsync();

    [Alias("GetProviderSendCallsAsync")]
    Task<int> GetProviderSendCallsAsync();
}
