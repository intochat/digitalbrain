using Orleans;
using Orleans.Concurrency;

namespace DigitalBrain.Runtime.Orleans.Synapses;

/// <summary>Durable list of brains with at least one synapse, so silo-start reconcile never reads local files. Single key <c>index</c>.</summary>
[Alias("brain.synapse-index")]
internal interface ISynapseRegistryIndex : IGrainWithStringKey
{
    Task Add(string brainKey);

    Task Remove(string brainKey);

    [ReadOnly]
    Task<string[]> List();
}
