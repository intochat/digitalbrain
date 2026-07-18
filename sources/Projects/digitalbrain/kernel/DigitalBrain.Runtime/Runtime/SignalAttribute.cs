namespace DigitalBrain.Runtime.Runtime;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class SignalAttribute(string identity) : Attribute
{
    public string Identity { get; } = identity;
}
