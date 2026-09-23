using DigitalBrain.Contracts;
using Orleans;
using Orleans.Concurrency;

namespace DigitalBrain.Flutter.Workspace;

[Alias("ui.workspace"), Orleans.Metadata.DefaultGrainType("ui.workspace")]
public interface IWorkspace : INeuron
{
    Task<WorkspaceOpenResult> Open(OpenWindow request);
    Task<WorkspaceState> OpenSurface(OpenSurfaceWindow request);
    Task<WorkspaceState> Close(string windowId, long expectedRevision);
    [ReadOnly] Task<WorkspaceState> Read();
}

[GenerateSerializer, Alias("ui.workspace-revision-conflict")]
public sealed class WorkspaceRevisionConflictException(string message, long currentRevision = 0) : InvalidOperationException(message)
{
    // Carries the observed revision so an optimistic caller can retry without a separate read.
    [Id(0)] public long CurrentRevision { get; } = currentRevision;
}