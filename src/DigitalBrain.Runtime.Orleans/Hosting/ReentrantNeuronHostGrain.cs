using DigitalBrain.Runtime.Orleans.Placement;
using Orleans;

using OrleansSdk = Orleans;
namespace DigitalBrain.Runtime.Orleans.Hosting;

/// <summary>The host of a [Reentrant] neuron class: every call interleaves; placed with the brain.</summary>
[GrainType("neuron.host.reentrant"), BrainAffinePlacement, OrleansSdk.Concurrency.Reentrant]
internal sealed class ReentrantNeuronHostGrain(NeuronTypeRegistry types, IGrainFactory grains) : NeuronHostGrainBase(types, grains)
{
    protected override string GrainTypeName => NeuronTypeRegistry.ReentrantGrainType;
}
