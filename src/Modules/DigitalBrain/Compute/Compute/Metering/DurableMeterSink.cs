namespace DigitalBrain.Compute.Metering;

internal sealed class DurableMeterSink(IMeterStore store) : IBatchMeterSink
{
    public async ValueTask RecordAsync(MeterEvent meterEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(meterEvent);
        await store.AppendAsync(meterEvent, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<int> RecordBatchAsync(IReadOnlyList<MeterEvent> meterEvents, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(meterEvents);
        return await store.AppendBatchAsync(meterEvents, cancellationToken).ConfigureAwait(false);
    }
}
