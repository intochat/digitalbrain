namespace DigitalBrain.Product.Approvals;

/// <summary>
/// A frozen, redacted approval review item. Executable action bindings remain in
/// the per-proposal approval authority and never enter the workspace inbox.
/// </summary>
public sealed record ApprovalWorkspaceInboxItem
{
    public ApprovalWorkspaceInboxItem(
        string proposalId,
        string proposalFingerprint,
        string title,
        string summary,
        IReadOnlyList<ApprovalEvidence> evidence,
        IReadOnlyList<ApprovalChange> changes,
        DateTimeOffset expiresAt,
        ApprovalReviewContext? context,
        ApprovalWorkspaceItemStatus status)
    {
        var pending = new ApprovalPending(
            proposalId,
            proposalFingerprint,
            title,
            summary,
            evidence,
            changes,
            expiresAt,
            context);
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status), status, "The workspace approval status is not recognized.");
        }

        ProposalId = pending.ProposalId;
        ProposalFingerprint = pending.ProposalFingerprint;
        Title = pending.Title;
        Summary = pending.Summary;
        Evidence = pending.Evidence;
        Changes = pending.Changes;
        ExpiresAt = pending.ExpiresAt;
        Context = pending.ReviewContext;
        Status = status;
    }

    public string ProposalId { get; }

    public string ProposalFingerprint { get; }

    public string Title { get; }

    public string Summary { get; }

    public IReadOnlyList<ApprovalEvidence> Evidence { get; }

    public IReadOnlyList<ApprovalChange> Changes { get; }

    public DateTimeOffset ExpiresAt { get; }

    public ApprovalReviewContext? Context { get; }

    public ApprovalWorkspaceItemStatus Status { get; }

    internal static ApprovalWorkspaceInboxItem Pending(ApprovalPending pending)
    {
        ArgumentNullException.ThrowIfNull(pending);
        return new ApprovalWorkspaceInboxItem(
            pending.ProposalId,
            pending.ProposalFingerprint,
            pending.Title,
            pending.Summary,
            pending.Evidence,
            pending.Changes,
            pending.ExpiresAt,
            pending.ReviewContext,
            ApprovalWorkspaceItemStatus.Pending);
    }

    internal ApprovalWorkspaceInboxItem WithStatus(ApprovalWorkspaceItemStatus status)
        => new(
            ProposalId,
            ProposalFingerprint,
            Title,
            Summary,
            Evidence,
            Changes,
            ExpiresAt,
            Context,
            status);
}
