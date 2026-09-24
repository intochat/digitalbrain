using DigitalBrain.Contracts;
using Orleans.Metadata;

namespace DigitalBrain.Apps;

// The app directory makes saved and installed apps discoverable: the Discovery module reads it as its
// manifest source. It always exposes the committed first-party manifests as global entries, and it
// records the owning workspace on every saved or installed manifest so discovery can scope search.
[Alias("app-manifest-directory"), DefaultGrainType("app-manifest-directory")]
public interface IAppManifestDirectory : INeuron
{
    Task<AppManifest> Publish(AppManifest manifest, string workspaceId);

    Task<IReadOnlyList<ScopedAppManifest>> Read();
}
