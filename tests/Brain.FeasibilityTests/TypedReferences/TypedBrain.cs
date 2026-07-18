using System.Reflection;
using Orleans;

namespace Brain.FeasibilityTests.TypedReferences;

public sealed class TypedBrain(IGrainFactory grainFactory)
{
    public T Get<T>(string organizationId, string spaceId, string instanceId)
        where T : IGrainWithStringKey
    {
        var key = NeuronIdentity.Derive(typeof(T), organizationId, spaceId, instanceId);
        return grainFactory.GetGrain<T>(key);
    }
}

public static class NeuronIdentity
{
    public static string Derive(Type contractType, string organizationId, string spaceId, string instanceId)
    {
        var contract = contractType.GetCustomAttribute<NeuronContractAttribute>()
            ?? throw new InvalidOperationException($"Missing {nameof(NeuronContractAttribute)} on {contractType.Name}.");
        return $"{organizationId}|{spaceId}|{contract.ContractId}/{instanceId}";
    }
}
