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

    [Alias("GetLastFailureAsync")]
    Task<SanitizedFailure?> GetLastFailureAsync();

    [Alias("GetActivationTokenAsync")]
    Task<Guid> GetActivationTokenAsync();

    [Alias("RequestDeactivationAsync")]
    Task RequestDeactivationAsync();

    [Alias("GetFeedStreamIdAsync")]
    Task<Guid> GetFeedStreamIdAsync();

    [Alias("SetFailNextOutcomePublishAsync")]
    Task SetFailNextOutcomePublishAsync(int count);

    [Alias("HasEffectTerminalAsync")]
    Task<bool> HasEffectTerminalAsync(string idempotencyKey);
}
