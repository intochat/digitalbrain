namespace DigitalBrain.Coding;

public sealed class WorkspaceNotReadyException(WorkspaceStatus status) : InvalidOperationException(status.Advice)
{
    public WorkspaceStatus Status { get; } = status;
}
