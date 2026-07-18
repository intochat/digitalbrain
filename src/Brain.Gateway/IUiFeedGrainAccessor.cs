using Brain.Contracts;

namespace Brain.Gateway;

public interface IUiFeedGrainAccessor
{
    IUiFeed GetFeed(OrganizationId organizationId, SpaceId spaceId);
}

public sealed class ClusterUiFeedGrainAccessor(IClusterClient clusterClient) : IUiFeedGrainAccessor
{
    public IUiFeed GetFeed(OrganizationId organizationId, SpaceId spaceId) =>
        clusterClient.GetGrain<IUiFeed>(IUiFeed.CreateGrainKey(organizationId, spaceId));
}
