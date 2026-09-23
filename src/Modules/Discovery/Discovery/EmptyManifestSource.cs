using DigitalBrain.Apps;

namespace DigitalBrain.Discovery;

internal sealed class EmptyManifestSource : IManifestSource
{
    public Task<IReadOnlyList<AppManifest>> ReadAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<AppManifest>>([]);
}
