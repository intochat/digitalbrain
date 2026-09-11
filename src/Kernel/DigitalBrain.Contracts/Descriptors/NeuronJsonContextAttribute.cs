namespace DigitalBrain.Abstractions.Descriptors;

[AttributeUsage(AttributeTargets.Assembly)]
public sealed class NeuronJsonContextAttribute(Type contextType) : Attribute
{
    public Type ContextType { get; } = contextType;
}
