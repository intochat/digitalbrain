namespace DigitalBrain.Compute.Metering;

internal interface IMeterStore
{
    ValueTask<bool> AppendAsync(MeterEvent meterEvent, CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<MeterEvent>> ReadAsync(CancellationToken cancellationToken = default);
}
