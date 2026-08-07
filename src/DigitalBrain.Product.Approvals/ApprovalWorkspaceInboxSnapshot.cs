namespace DigitalBrain.Product.Approvals;

public sealed record ApprovalWorkspaceInboxSnapshot : Synapse
{
    public ApprovalWorkspaceInboxSnapshot(
        long revision,
        IReadOnlyList<ApprovalWorkspaceInboxItem> items)
    {
        if (revision <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(revision), revision, "An inbox snapshot needs a positive revision.");
        }

        ArgumentNullException.ThrowIfNull(items);
        var itemCopies = items.Select(static item =>
        {
            ArgumentNullException.ThrowIfNull(item);
            return new ApprovalWorkspaceInboxItem(
                item.ProposalId,
                item.ProposalFingerprint,
                item.Title,
                item.Summary,
                item.Evidence,
                item.Changes,
                item.ExpiresAt,
                item.Context,
                item.Status);
        }).ToArray();

        Revision = revision;
        Items = Array.AsReadOnly(itemCopies);
    }

    public long Revision { get; }

    public IReadOnlyList<ApprovalWorkspaceInboxItem> Items { get; }
}
