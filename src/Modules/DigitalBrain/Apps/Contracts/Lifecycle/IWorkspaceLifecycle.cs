using DigitalBrain.Contracts;
using Orleans.Metadata;

namespace DigitalBrain.Apps;

[Alias("brain.workspace-lifecycle"), DefaultGrainType("brain.workspace-lifecycle")]
public interface IWorkspaceLifecycle : INeuron
{
    Task<WorkspaceActivation> Read();
    Task<WorkspaceActivation> Activate();
}
