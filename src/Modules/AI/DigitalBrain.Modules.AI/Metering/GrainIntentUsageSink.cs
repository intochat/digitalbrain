using DigitalBrain.Compute;
using DigitalBrain.Contracts;
using Orleans;

namespace DigitalBrain.AI.Metering;

/// <summary>
/// Accumulates metered entries in the ambient <see cref="IntentContext"/> and writes the whole
/// intent's usage to the durable per-intent neuron in one batch on flush. A direct call without an
/// ambient intent (for example a test or a background embedding) still persists immediately.
/// When the Compute module is loaded, the same batch is converted into idempotent meter events.
/// </summary>
internal sealed class GrainIntentUsageSink(IGrainFactory grains, IMeterSink? meterSink = null) : IIntentUsageSink
{
    public async Task RecordAsync(string intentId, TokenUsageEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(intentId);
        ArgumentNullException.ThrowIfNull(entry);
        if (IntentContext.Current is { } intent && string.Equals(intent.IntentId, intentId, StringComparison.Ordinal))
        {
            intent.AddUsage(entry);
            return;
        }

        await grains.GetGrain<IIntentUsage>(intentId).RecordAsync(entry, cancellationToken).ConfigureAwait(false);
        await EmitAsync(intentId, null, [entry], cancellationToken).ConfigureAwait(false);
    }

    public async Task FlushAsync(IntentContext intent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent);
        var entries = intent.Usage.OfType<TokenUsageEntry>().ToArray();
        if (entries.Length == 0) { return; }
        await grains.GetGrain<IIntentUsage>(intent.IntentId).RecordBatchAsync(entries, cancellationToken).ConfigureAwait(false);
        await EmitAsync(intent.IntentId, intent.ScopeId, entries, cancellationToken).ConfigureAwait(false);
    }

    private async Task EmitAsync(string intentId, string? workspaceId, TokenUsageEntry[] entries, CancellationToken cancellationToken)
    {
        if (meterSink is null) { return; }
        var meterEvents = IntentMeterEvents.FromUsage(intentId, workspaceId, entries).ToArray();
        if (meterEvents.Length == 0) { return; }
        try
        {
            // One intent's events are one durable write, keyed per (IntentId, MeterId, Step).
            if (meterSink is IBatchMeterSink batch)
            {
                await batch.RecordBatchAsync(meterEvents, cancellationToken).ConfigureAwait(false);
                return;
            }

            foreach (var meterEvent in meterEvents)
            {
                await meterSink.RecordAsync(meterEvent, cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
            // Metering is an observer: a Compute store fault must never fail provider usage capture.
        }
    }
}
