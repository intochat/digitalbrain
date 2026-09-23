using System.Collections.Concurrent;

namespace DigitalBrain.Compute.Metering;

// Append-only meter store with the idempotency key (IntentId, MeterId, Step) as its
// uniqueness constraint. A repeated key is accepted and discarded, never overwritten.
internal sealed class InMemoryMeterStore : IMeterStore
{
    private readonly ConcurrentDictionary<(string IntentId, string MeterId, string Step), MeterEvent> events = new();

    public ValueTask<bool> AppendAsync(MeterEvent meterEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(meterEvent);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(events.TryAdd(Key(meterEvent), meterEvent));
    }

    public ValueTask<IReadOnlyList<MeterEvent>> ReadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<MeterEvent> snapshot = [.. events.Values.OrderBy(meter => meter.OccurredAt)];
        return ValueTask.FromResult(snapshot);
    }

    internal static (string IntentId, string MeterId, string Step) Key(MeterEvent meterEvent)
        => (meterEvent.IntentId, meterEvent.MeterId, meterEvent.Step);
}
