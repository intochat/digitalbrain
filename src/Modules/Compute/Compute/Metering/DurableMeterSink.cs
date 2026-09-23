namespace DigitalBrain.Compute.Metering;

internal sealed class DurableMeterSink(IMeterStore store) : IMeterSink
{
    public async ValueTask RecordAsync(MeterEvent meterEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(meterEvent);
        await store.AppendAsync(meterEvent, cancellationToken).ConfigureAwait(false);
    }
}
