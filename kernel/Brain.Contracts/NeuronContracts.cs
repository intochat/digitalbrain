namespace Brain.Contracts;

public interface INeuronContract;

public interface IRawJson
{
    string Json { get; }
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class NeuronContractAttribute(string contract) : Attribute
{
    public string Contract { get; } = contract;
}
