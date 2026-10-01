using DigitalBrain.Contracts;
using Orleans.Metadata;

namespace DigitalBrain.Apps;

// The per-workspace app catalog. Key the grain by the workspace scope id.
[Alias("app-catalog"), DefaultGrainType("app-catalog")]
public interface IAppCatalog : INeuron
{
    Task<AppInstallation> Install(AppManifest manifest);

    Task<AppInstallation> Rollback(string appId);

    Task<AppUninstallOutcome> Uninstall(string appId);

    Task<IReadOnlyList<AppInstallation>> List();

    Task<AppInstallation?> Read(string appId);
}
