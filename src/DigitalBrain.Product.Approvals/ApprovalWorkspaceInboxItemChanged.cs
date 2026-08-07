namespace DigitalBrain.Product.Approvals;

public sealed record ApprovalWorkspaceInboxItemChanged : Synapse
{
    public ApprovalWorkspaceInboxItemChanged(ApprovalWorkspaceInboxItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        Item = new ApprovalWorkspaceInboxItem(
            item.ProposalId,
            item.ProposalFingerprint,
            item.Title,
            item.Summary,
            item.Evidence,
            item.Changes,
            item.ExpiresAt,
            item.Context,
            item.Status);
    }

    public ApprovalWorkspaceInboxItem Item { get; }
}
