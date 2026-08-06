namespace DigitalBrain;

internal sealed class OrleansJournalReader(IGrainFactory grains) : JournalReader
{
    public async Task<JournalRead> ReadAsync(
        NeuronId neuron,
        long afterPosition,
        int maximumRecords,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumRecords, 1);
        cancellationToken.ThrowIfCancellationRequested();

        var host = grains.GetGrain<INeuronHost>(NeuronHost.AddressOf(neuron));
        return await host.ReadAsync(afterPosition, maximumRecords).WaitAsync(cancellationToken);
    }
}
