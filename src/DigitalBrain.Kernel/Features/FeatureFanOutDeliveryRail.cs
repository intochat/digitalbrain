using DigitalBrain.Kernel.Contracts;
using Orleans;

namespace DigitalBrain.Kernel.Features;

internal sealed record FeatureDeliveryAttempt(FeatureInstallationId InstallationId, FeatureAppendStatus? Status);

internal sealed class FeatureFanOutDeliveryRail(IFeatureGrainResolver grains)
{
    public Task<FeatureDeliveryAttempt[]> DispatchAsync(BrainOwnerId ownerId, FeatureFanOutState batch) =>
        Task.WhenAll(batch.Deliveries.Where(delivery => !delivery.Delivered)
            .Select(delivery => DeliverAsync(ownerId, delivery.InstallationId, batch.Input)));

    private async Task<FeatureDeliveryAttempt> DeliverAsync(BrainOwnerId ownerId, FeatureInstallationId installationId, FeatureInput input)
    {
        try
        {
            var status = await grains.Installation(ownerId, installationId).AppendAsync(input);
            return new FeatureDeliveryAttempt(installationId, status);
        }
        catch
        {
            return new FeatureDeliveryAttempt(installationId, null);
        }
    }
}

internal sealed class OrleansFeatureGrainResolver(IGrainFactory grainFactory) : IFeatureGrainResolver
{
    public IFeatureHubGrain Hub(BrainOwnerId ownerId) =>
        grainFactory.GetGrain<IFeatureHubGrain>(FeatureGrainIds.Hub(ownerId));

    public IFeatureInstallationGrain Installation(BrainOwnerId ownerId, FeatureInstallationId installationId) =>
        grainFactory.GetGrain<IFeatureInstallationGrain>(FeatureGrainIds.Installation(ownerId, installationId));
}
