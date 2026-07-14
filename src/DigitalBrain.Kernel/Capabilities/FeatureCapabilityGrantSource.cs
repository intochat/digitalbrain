using System.Text.Json;
using DigitalBrain.Kernel.Contracts;
using Orleans;

namespace DigitalBrain.Kernel.Capabilities;

public sealed class FeatureCapabilityGrantSource(IGrainFactory grains) : ICapabilityGrantSource
{
    public async ValueTask<CapabilityGrant?> ReadAsync(
        CapabilityRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var hub = grains.GetGrain<IFeatureHubGrain>(FeatureGrainIds.Hub(request.OwnerId));
        var snapshot = await hub.ReadGrantAsync(new FeatureGrantLookup(
                request.InstallationId,
                request.ReleaseDigest,
                request.CapabilityId,
                request.CapabilityVersion))
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        if (snapshot is null || snapshot.ActorId != request.ActorId ||
            snapshot.Revision != request.GrantRevision ||
            snapshot.Grant.ProviderConnectionId != request.ProviderConnectionId)
            return null;
        using var constraints = JsonDocument.Parse(snapshot.Grant.ConstraintsJson);
        return new CapabilityGrant(
            request.OwnerId,
            snapshot.InstallationId,
            snapshot.Release,
            snapshot.Grant.CapabilityId,
            snapshot.Grant.CapabilityVersion,
            snapshot.Grant.ProviderConnectionId,
            snapshot.Revision,
            constraints.RootElement,
            enabled: true,
            paused: snapshot.Paused);
    }
}

public sealed class RuntimeCapabilityGrantSource(
    RetainedInoCapabilityGrantSource retained,
    FeatureCapabilityGrantSource features) : ICapabilityGrantSource
{
    public async ValueTask<CapabilityGrant?> ReadAsync(
        CapabilityRequest request,
        CancellationToken cancellationToken = default)
    {
        var retainedGrant = await retained.ReadAsync(request, cancellationToken).ConfigureAwait(false);
        return retainedGrant ?? await features.ReadAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
