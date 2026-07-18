using DigitalBrain.V2.Core.Runtime;
using DigitalBrain.V2.Core.Synapses;

namespace DigitalBrain.V2.Core.Brain;

// The brain is itself a neuron. Its one job is the single outward verb of the whole system:
// fire a synapse into the substrate. Broadcast vs point-to-point is decided by the synapse's
// own Routing header — set by the caller's verb (emit / ask / reply).
public interface IDigitalBrain : INeuron
{
    Task Fire(Synapse synapse, CancellationToken ct = default);
}
