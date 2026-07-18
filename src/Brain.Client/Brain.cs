using Brain.Contracts;

namespace Brain.Client;

public sealed class Brain(IClusterClient client)
{
    public T Get<T>(OrganizationId organizationId, SpaceId spaceId, string instanceId)
        where T : IGrainWithStringKey
    {
        var key = NeuronIdentity.Derive(typeof(T), organizationId, spaceId, instanceId);
        return client.GetGrain<T>(key);
    }
}
