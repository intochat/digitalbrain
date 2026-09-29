using DigitalBrain.Contracts;
using Orleans.Metadata;

namespace DigitalBrain.Apps;

// The app directory exposes committed first-party manifests globally and records the owning
// workspace on every saved or installed manifest so callers can respect workspace visibility.
[Alias("app-manifest-directory"), DefaultGrainType("app-manifest-directory")]
public interface IAppManifestDirectory : INeuron
{
    Task<AppManifest> Publish(AppManifest manifest, string workspaceId);

    Task<IReadOnlyList<ScopedAppManifest>> Read();
}