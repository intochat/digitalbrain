using DigitalBrain.V2.Core.Synapses;
using Orleans.Concurrency;

namespace DigitalBrain.V2.Core.Runtime;

// Base grain contract every neuron exposes. Point-to-point delivery and an activation
// primer (so a subscribing neuron is live before a broadcast is fired at it).
public interface INeuron : IGrainWithStringKey
{
    [AlwaysInterleave]
    Task DeliverAsync(Synapse synapse);
    Task EnsureActiveAsync();
}

// Wiring manifest, declared on the Contracts INTERFACE (not the impl) so the whole
// constellation graph is scannable without loading or running any implementation.
//   IHandle<T> = in-edge  (this neuron consumes T)
//   IEmit<T>   = out-edge (this neuron can fire T; broadcast or directed)
public interface IHandle<in TSynapse> where TSynapse : Synapse
{
    Task HandleAsync(TSynapse synapse, CancellationToken ct);
}

public interface IEmit<TSynapse> where TSynapse : Synapse;
