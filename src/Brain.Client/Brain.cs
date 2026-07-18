using Brain.Contracts;

namespace Brain.Client;

public sealed class Brain(IGrainFactory grainFactory)
{
    public T Get<T>(OrganizationId organizationId, SpaceId spaceId, string instanceId)
        where T : IGrainWithStringKey
    {
        var key = NeuronIdentity.Derive(typeof(T), organizationId, spaceId, instanceId);
        return grainFactory.GetGrain<T>(key);
    }
}
