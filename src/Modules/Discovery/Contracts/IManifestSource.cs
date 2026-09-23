using DigitalBrain.Apps;

namespace DigitalBrain.Discovery;

// The manifest set is the truth; the catalog only indexes what the source supplies.
public interface IManifestSource
{
    Task<IReadOnlyList<AppManifest>> ReadAsync(CancellationToken cancellationToken = default);
}
