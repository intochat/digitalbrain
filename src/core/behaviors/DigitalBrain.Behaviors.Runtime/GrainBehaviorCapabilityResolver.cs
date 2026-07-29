using DigitalBrain.Abstractions;

namespace DigitalBrain.Behaviors;

internal sealed class GrainBehaviorCapabilityResolver(IGrainFactory grains, OwnerId owner) : IBehaviorCapabilityResolver
{
    public TContract Get<TContract>(string name)
        where TContract : class, INeuron
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return grains.GetGrain<TContract>(NeuronId.For<TContract>(owner, name).ToGrainId());
    }
}
