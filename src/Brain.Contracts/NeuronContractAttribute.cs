namespace Brain.Contracts;

[AttributeUsage(AttributeTargets.Interface, AllowMultiple = false, Inherited = false)]
public sealed class NeuronContractAttribute(string contractId) : Attribute
{
    public string ContractId { get; } = contractId;
}
