using DigitalBrain.Contracts;
using Orleans.Concurrency;

namespace DigitalBrain.Flutter.Workspace;

[Alias("ui.workspace"), Orleans.Metadata.DefaultGrainType("ui.workspace")]
public interface IWorkspace : INeuron
{
    Task<WorkspaceState> Open(OpenWindow request);
    Task<WorkspaceState> OpenSurface(OpenSurfaceWindow request);
    Task<WorkspaceState> Close(string windowId, long expectedRevision);
    [ReadOnly] Task<WorkspaceState> Read();
}

[GenerateSerializer, Alias("ui.workspace-revision-conflict")]
public sealed class WorkspaceRevisionConflictException(string message) : InvalidOperationException(message);