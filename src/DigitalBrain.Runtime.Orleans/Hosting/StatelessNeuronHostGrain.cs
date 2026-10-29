using Orleans;
using Orleans.Concurrency;

namespace DigitalBrain.Runtime.Orleans.Hosting;

/// <summary>The host of a [StatelessNeuron] class: many activations per key, calls routed to any of them.</summary>
[GrainType("neuron.host.stateless"), StatelessWorker]
internal sealed class StatelessNeuronHostGrain(NeuronTypeRegistry types, IGrainFactory grains) : NeuronHostGrainBase(types, grains)
{
    protected override string GrainTypeName => NeuronTypeRegistry.StatelessGrainType;
}
