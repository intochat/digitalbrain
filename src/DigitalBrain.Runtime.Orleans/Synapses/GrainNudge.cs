using DigitalBrain.Units;
using DigitalBrain.Kernel;
using DigitalBrain.Runtime.Orleans.Hosting;
using Orleans;
using Orleans.Runtime;

namespace DigitalBrain.Runtime.Orleans.Synapses;

internal sealed class GrainNudge(IGrainFactory grains, NeuronTypeRegistry types, PublishFaults? faults = null) : INudge
{
    public void Nudge(NeuronId subscriber, Position upTo)
    {
        if (faults?.FailAfterAppend == true)
        {
            throw new InvalidOperationException("PublishFaults: FailAfterAppend.");
        }
        var grainType = types.TryGetRegistration(subscriber.Type, out var registration)
            ? NeuronTypeRegistry.GrainTypeOf(registration.Policy)
            : NeuronTypeRegistry.AffineGrainType;
        var grainId = GrainId.Create(grainType, subscriber.ToString());
        var host = grains.GetGrain<ISynapseHost>(grainId);
        _ = host.Nudge(upTo.Value);
    }
}
