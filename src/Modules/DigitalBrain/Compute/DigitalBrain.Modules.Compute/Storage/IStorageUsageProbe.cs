namespace DigitalBrain.Compute.Storage;

internal interface IStorageUsageProbe
{
    ValueTask<long> ReadBytesAsync(CancellationToken cancellationToken = default);
}
