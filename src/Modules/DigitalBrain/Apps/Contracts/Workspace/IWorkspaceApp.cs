using DigitalBrain.Contracts;
using Orleans.Metadata;

namespace DigitalBrain.Apps;

[Alias("apps.workspace-app"), DefaultGrainType("apps.workspace-app")]
public interface IWorkspaceApp : INeuron
{
    Task<AppSnapshot> Install(AppManifest manifest, IReadOnlyDictionary<string, string> configuration);
    Task<AppSnapshot> Read();
    Task<AppSnapshot> Configure(IReadOnlyDictionary<string, string> configuration);
    Task<AppSnapshot> Activate();
    Task<AppSnapshot> WorkspaceActivated(long generation);
    Task<AppSnapshot> Deactivate();
    Task<AppSnapshot> Dispatch(AppDispatch request);
}
