using DigitalBrain.Apps;

namespace DigitalBrain.Discovery;

// Reads the app manifest directory, so first-party manifests plus every saved or installed app are
// searchable, each carrying the workspace scope the directory recorded for it.
internal sealed class AppsManifestSource(IGrainFactory grains) : IManifestSource
{
    public async Task<IReadOnlyList<ScopedAppManifest>> ReadAsync(CancellationToken cancellationToken = default)
        => await grains.GetGrain<IAppManifestDirectory>(AppManifestDirectoryGrains.Key).Read().ConfigureAwait(false);
}
