using System.Reflection;
using Brain.Contracts;

namespace Brain.Client;

public static class NeuronIdentity
{
    public static string Derive(Type contractType, OrganizationId organizationId, SpaceId spaceId, string instanceId)
    {
        var contract = contractType.GetCustomAttribute<NeuronContractAttribute>()
            ?? throw new InvalidOperationException($"Missing {nameof(NeuronContractAttribute)} on {contractType.Name}.");
        return new NeuronAddress(organizationId, spaceId, contract.ContractId, instanceId).ToGrainKey();
    }
}
