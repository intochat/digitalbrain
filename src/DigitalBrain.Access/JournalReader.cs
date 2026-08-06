namespace DigitalBrain;

public interface JournalReader
{
    Task<JournalRead> ReadAsync(
        NeuronId neuron,
        long afterPosition,
        int maximumRecords,
        CancellationToken cancellationToken = default);
}
