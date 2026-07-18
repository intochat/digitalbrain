using DigitalBrain.AI;
using DigitalBrain.Google;
using DigitalBrain.Salesforce;

namespace Brain.Gateway;

public sealed class TypedSurfaceOwnerResolver : ISurfaceOwnerResolver
{
    private readonly ITypedNeuronLookup _neurons;

    public TypedSurfaceOwnerResolver(IClusterClient clusterClient)
        : this(new ClusterTypedNeuronLookup(clusterClient))
    {
    }

    public TypedSurfaceOwnerResolver(ITypedNeuronLookup neurons) => _neurons = neurons;

    public ISurfaceOwner Resolve(string contractId, string instanceId) =>
        contractId switch
        {
            KnownSurfaceContracts.GroupChat => new GroupChatSurfaceOwner(_neurons.GetGroupChat(instanceId)),
            KnownSurfaceContracts.Gmail => new GmailSurfaceOwner(_neurons.GetGmail(instanceId)),
            KnownSurfaceContracts.Salesforce => new SalesforceSurfaceOwner(_neurons.GetSalesforce(instanceId)),
            _ => throw new InvalidOperationException($"Unknown surface contract '{contractId}'.")
        };
}
