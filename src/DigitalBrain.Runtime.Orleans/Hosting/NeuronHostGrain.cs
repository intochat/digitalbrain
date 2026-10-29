using DigitalBrain.Runtime.Orleans.Placement;
using Orleans;

namespace DigitalBrain.Runtime.Orleans.Hosting;

/// <summary>The default host: ordinary turns serialize; validated interleaved endpoints use Orleans' explicit flag.</summary>
[GrainType("neuron.host"), BrainAffinePlacement]
internal sealed class NeuronHostGrain(NeuronTypeRegistry types, IGrainFactory grains) : NeuronHostGrainBase(types, grains)
{
    protected override string GrainTypeName => NeuronTypeRegistry.AffineGrainType;
}
