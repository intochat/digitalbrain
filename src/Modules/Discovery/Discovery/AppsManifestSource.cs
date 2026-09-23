using DigitalBrain.Apps;

namespace DigitalBrain.Discovery;

// Reads the app manifest directory, so first-party manifests plus every saved or installed app
// are searchable without discovery knowing which workspace saved them.
internal sealed class AppsManifestSource(IGrainFactory grains) : IManifestSource
{
    public async Task<IReadOnlyList<AppManifest>> ReadAsync(CancellationToken cancellationToken = default)
        => await grains.GetGrain<IAppManifestDirectory>(AppManifestDirectoryGrains.Key).Read().ConfigureAwait(false);
}
