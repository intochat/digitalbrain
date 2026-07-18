using DigitalBrain.AI;
using DigitalBrain.Google;
using DigitalBrain.Salesforce;

namespace Brain.Gateway;

public interface ITypedNeuronLookup
{
    IGroupChat GetGroupChat(string instanceId);
    IGmail GetGmail(string instanceId);
    ISalesforce GetSalesforce(string instanceId);
}

public sealed class ClusterTypedNeuronLookup(IClusterClient clusterClient) : ITypedNeuronLookup
{
    private Brain.Client.Brain BrainClient => new(clusterClient);

    public IGroupChat GetGroupChat(string instanceId) =>
        BrainClient.Get<IGroupChat>(DevelopmentPrincipal.OrganizationId, DevelopmentPrincipal.SpaceId, instanceId);

    public IGmail GetGmail(string instanceId) =>
        BrainClient.Get<IGmail>(DevelopmentPrincipal.OrganizationId, DevelopmentPrincipal.SpaceId, instanceId);

    public ISalesforce GetSalesforce(string instanceId) =>
        BrainClient.Get<ISalesforce>(DevelopmentPrincipal.OrganizationId, DevelopmentPrincipal.SpaceId, instanceId);
}
