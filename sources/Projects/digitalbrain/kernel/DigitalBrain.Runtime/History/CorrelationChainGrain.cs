using DigitalBrain.Runtime.Neurons;
using Orleans.Journaling;

namespace DigitalBrain.Runtime.History;

internal sealed class CorrelationChainGrain(
    [FromKeyedServices("chain")] IDurableList<Synapse> chain)
    : DurableGrain, ICorrelationChain
{
    public async Task AppendAsync(Synapse synapse, CancellationToken ct)
    {
        chain.Add(synapse);
        await WriteStateAsync(ct);
    }

    public Task<IReadOnlyList<Synapse>> SnapshotAsync(CancellationToken ct)
        => Task.FromResult<IReadOnlyList<Synapse>>(chain.ToArray());

    public Task<int> CountAsync(CancellationToken ct) => Task.FromResult(chain.Count);
}
