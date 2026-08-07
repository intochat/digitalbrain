namespace DigitalBrain.Product.Presentation;

public sealed class ApprovalWorkspaceProjectionState
{
    public long Revision { get; set; }

    public IReadOnlyList<ApprovalWorkspaceSurfaceItem> Items { get; set; } = [];
}
