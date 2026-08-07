using DigitalBrain.Product.Approvals;

namespace DigitalBrain.Product.Presentation;

public sealed record ApprovalWorkspaceSurfaceItem
{
    public ApprovalWorkspaceSurfaceItem(
        string proposalId,
        string proposalFingerprint,
        string title,
        string summary,
        IReadOnlyList<ApprovalEvidence> evidence,
        IReadOnlyList<ApprovalChange> changes,
        DateTimeOffset expiresAt,
        ApprovalReviewContext? context,
        ApprovalWorkspaceItemStatus status,
        IReadOnlyList<ApprovalReviewPlacement> placements,
        IReadOnlyList<ApprovalWorkspaceSurfaceAction> actions)
    {
        var safeItem = new ApprovalWorkspaceInboxItem(
            proposalId,
            proposalFingerprint,
            title,
            summary,
            evidence,
            changes,
            expiresAt,
            context,
            status);
        ArgumentNullException.ThrowIfNull(placements);
        ArgumentNullException.ThrowIfNull(actions);
        var placementCopy = placements.ToArray();
        var actionCopy = actions.ToArray();
        if (placementCopy.Length == 0
            || placementCopy.Any(static placement => !Enum.IsDefined(placement))
            || placementCopy.Distinct().Count() != placementCopy.Length)
        {
            throw new ArgumentException("A workspace surface item needs distinct, recognized placements.", nameof(placements));
        }

        if (actionCopy.Length == 0
            || actionCopy.Any(static action => action is null)
            || actionCopy.Select(static action => action.Decision).Distinct().Count() != actionCopy.Length)
        {
            throw new ArgumentException("A workspace surface item needs distinct approval actions.", nameof(actions));
        }

        ProposalId = safeItem.ProposalId;
        ProposalFingerprint = safeItem.ProposalFingerprint;
        Title = safeItem.Title;
        Summary = safeItem.Summary;
        Evidence = safeItem.Evidence;
        Changes = safeItem.Changes;
        ExpiresAt = safeItem.ExpiresAt;
        Context = safeItem.Context;
        Status = safeItem.Status;
        Placements = Array.AsReadOnly(placementCopy);
        Actions = Array.AsReadOnly(actionCopy);
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

    public IReadOnlyList<ApprovalReviewPlacement> Placements { get; }

    public IReadOnlyList<ApprovalWorkspaceSurfaceAction> Actions { get; }
}
