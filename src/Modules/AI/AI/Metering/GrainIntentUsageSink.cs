using DigitalBrain.Contracts;
using Orleans;

namespace DigitalBrain.AI.Metering;

/// <summary>
/// Accumulates metered entries in the ambient <see cref="IntentContext"/> and writes the whole
/// intent's usage to the durable per-intent neuron in one batch on flush. A direct call without an
/// ambient intent (for example a test or a background embedding) still persists immediately.
/// </summary>
internal sealed class GrainIntentUsageSink(IGrainFactory grains) : IIntentUsageSink
{
    public Task RecordAsync(string intentId, TokenUsageEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(intentId);
        ArgumentNullException.ThrowIfNull(entry);
        if (IntentContext.Current is { } intent && string.Equals(intent.IntentId, intentId, StringComparison.Ordinal))
        {
            intent.AddUsage(entry);
            return Task.CompletedTask;
        }

        return grains.GetGrain<IIntentUsage>(intentId).RecordAsync(entry, cancellationToken);
    }

    public Task FlushAsync(IntentContext intent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent);
        var entries = intent.Usage.OfType<TokenUsageEntry>().ToArray();
        return entries.Length == 0
            ? Task.CompletedTask
            : grains.GetGrain<IIntentUsage>(intent.IntentId).RecordBatchAsync(entries, cancellationToken);
    }
}