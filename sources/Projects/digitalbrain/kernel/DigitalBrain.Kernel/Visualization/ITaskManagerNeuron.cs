using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.Kernel.Visualization;

// Cluster-singleton kernel-side neuron grain. Keyed by Guid.Empty (same
// convention as TimelineRelayGrain) — there is exactly one task projection
// per cluster, and the observer + ticker both target that single key.
public interface ITaskManagerNeuron : INeuron
{
    Task Observe(Synapse synapse);
    Task Tick();
    Task<IReadOnlyList<Guid>> GetActiveCorrelationIdsAsync();
}
