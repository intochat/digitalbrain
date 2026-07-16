namespace Brain.Contracts;

public interface INeuronContract;

[AttributeUsage(AttributeTargets.Method)]
public sealed class NeuronContractAttribute(string contract) : Attribute
{
    public string Contract { get; } = contract;
}
