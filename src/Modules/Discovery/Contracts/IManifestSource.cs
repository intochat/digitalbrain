using DigitalBrain.Apps;

namespace DigitalBrain.Discovery;

// The manifest set is the truth; the catalog only indexes what the source supplies. Each entry
// carries its install scope so the catalog can keep one workspace's apps out of another's search.
public interface IManifestSource
{
    Task<IReadOnlyList<ScopedAppManifest>> ReadAsync(CancellationToken cancellationToken = default);
}
