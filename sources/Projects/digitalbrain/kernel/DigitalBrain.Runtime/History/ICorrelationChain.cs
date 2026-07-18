using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.Runtime.History;

public interface ICorrelationChain : IGrainWithGuidKey
{
    Task AppendAsync(Synapse synapse, CancellationToken ct);
    Task<IReadOnlyList<Synapse>> SnapshotAsync(CancellationToken ct);
    Task<int> CountAsync(CancellationToken ct);
}
