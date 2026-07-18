using DigitalBrain.Runtime;
using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.Runtime.Catalog;

public interface IBrainTimelineRelay : IGrainWithGuidKey
{
    Task<SynapseSlice> WatchSinceAsync(long cursor);
    Task<IReadOnlyList<Synapse>> SnapshotAsync(DateTimeOffset since);
    Task<IReadOnlyList<CatalogedNeuron>> ListSeenAsync();
}
