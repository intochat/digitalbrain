using DigitalBrain.Discovery;

namespace DigitalBrain.Apps.Manifests;

// Discovery indexes the same committed first-party manifests the launcher and consent sheet read.
internal sealed class FirstPartyManifestSource : IManifestSource
{
    public Task<IReadOnlyList<ScopedAppManifest>> ReadAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<ScopedAppManifest>>(FirstPartyApps.All().Select(ScopedAppManifest.Global).ToList());
}
