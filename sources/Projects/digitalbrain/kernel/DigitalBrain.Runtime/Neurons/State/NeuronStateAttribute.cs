namespace DigitalBrain.Runtime.Neurons.State
{
    [AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class NeuronStateAttribute : Attribute, IFacetMetadata;
}

namespace DigitalBrain.Core.Neurons
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class StateAttribute : Attribute;
}
