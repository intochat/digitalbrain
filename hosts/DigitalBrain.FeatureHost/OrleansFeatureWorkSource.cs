using DigitalBrain.Kernel.Contracts;
using Orleans;

namespace DigitalBrain.FeatureHost;

public sealed class OrleansFeatureWorkSource : IFeatureWorkSource
{
    private static readonly TimeSpan EmptyPollDelay = TimeSpan.FromSeconds(1);
    private readonly IFeatureArtifactCatalog _artifacts;
    private readonly FeatureReleaseManager _releases;
    private readonly IClusterClient _cluster;
    private int _cursor;

    public OrleansFeatureWorkSource(
        IFeatureArtifactCatalog artifacts,
        FeatureReleaseManager releases,
        IClusterClient cluster)
    {
        _artifacts = artifacts ?? throw new ArgumentNullException(nameof(artifacts));
        _releases = releases ?? throw new ArgumentNullException(nameof(releases));
        _cluster = cluster ?? throw new ArgumentNullException(nameof(cluster));
    }

    public async ValueTask<FeatureWorkItem> TakeAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            var active = await _artifacts.ReadActiveAsync(cancellationToken);
            await _releases.LoadActiveAsync(
                active.Select(item => new FeatureActiveInstallation(
                    item.OwnerId,
                    item.InstallationId,
                    item.Release)).ToArray(),
                cancellationToken);
            for (var offset = 0; offset < active.Count; offset++)
            {
                var index = Math.Abs(Interlocked.Increment(ref _cursor) + offset) % active.Count;
                var candidate = active[index];
                var grain = _cluster.GetGrain<IFeatureInstallationGrain>(
                    FeatureGrainIds.Installation(candidate.OwnerId, candidate.InstallationId));
                var snapshot = await grain.ReadAsync();
                if (snapshot.Paused || snapshot.Inbox.Length == 0 || snapshot.ActiveRelease != candidate.Release.Digest)
                    continue;
                return new FeatureWorkItem(candidate.InstallationId, grain)
                {
                    OwnerId = candidate.OwnerId,
                    ActorId = candidate.ActorId,
                    GrantRevision = candidate.GrantRevision,
                    ProviderConnections = candidate.ProviderConnections
                };
            }
            await Task.Delay(EmptyPollDelay, cancellationToken);
        }
    }
}
