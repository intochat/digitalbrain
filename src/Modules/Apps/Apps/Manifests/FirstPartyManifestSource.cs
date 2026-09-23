using DigitalBrain.Discovery;

namespace DigitalBrain.Apps.Manifests;

// Discovery indexes the same committed first-party manifests the launcher and consent sheet read.
internal sealed class FirstPartyManifestSource : IManifestSource
{
    public Task<IReadOnlyList<AppManifest>> ReadAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(FirstPartyApps.All());
}
