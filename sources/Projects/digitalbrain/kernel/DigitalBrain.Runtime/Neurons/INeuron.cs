namespace DigitalBrain.Runtime.Neurons;

public interface INeuron : IGrainWithGuidKey
{
    Task<IReadOnlyList<Synapse>> GetIncomingJournalAsync(int fromIndex = 0, int toIndex = int.MaxValue);
    Task<IReadOnlyList<Synapse>> GetOutgoingJournalAsync(int fromIndex = 0, int toIndex = int.MaxValue);
    Task<int> GetIncomingCountAsync();
    Task<int> GetOutgoingCountAsync();
}