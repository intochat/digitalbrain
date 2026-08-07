namespace DigitalBrain.Product.Approvals;

public sealed class ApprovalWorkspaceInboxState
{
    public long Revision { get; set; }

    public IReadOnlyList<ApprovalWorkspaceInboxItem> Items { get; set; } = [];
}
