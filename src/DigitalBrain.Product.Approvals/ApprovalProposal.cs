namespace DigitalBrain.Product.Approvals;

public sealed record ApprovalProposal
{
    public ApprovalProposal(
        string proposalId,
        string title,
        string summary,
        IReadOnlyList<ApprovalEvidence> evidence,
        IReadOnlyList<ApprovalChange> changes,
        ApprovalActionBinding action,
        DateTimeOffset expiresAt,
        ApprovalReviewContext? reviewContext = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(proposalId);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentNullException.ThrowIfNull(action);
        if (expiresAt == default)
        {
            throw new ArgumentException("An approval proposal needs an expiry.", nameof(expiresAt));
        }

        var evidenceCopy = evidence.ToArray();
        var changesCopy = changes.ToArray();
        if (evidenceCopy.Any(static item => item is null))
        {
            throw new ArgumentException("Approval evidence cannot contain null entries.", nameof(evidence));
        }

        if (changesCopy.Any(static item => item is null))
        {
            throw new ArgumentException("Approval changes cannot contain null entries.", nameof(changes));
        }

        ProposalId = proposalId.Trim();
        Title = title.Trim();
        Summary = summary.Trim();
        Evidence = Array.AsReadOnly(evidenceCopy);
        Changes = Array.AsReadOnly(changesCopy);
        Action = action;
        ExpiresAt = expiresAt;
        ReviewContext = reviewContext;
        Fingerprint = ApprovalFingerprint.Compute(
            ProposalId,
            Title,
            Summary,
            Evidence,
            Changes,
            Action,
            ExpiresAt,
            ReviewContext);
    }

    public string ProposalId { get; }

    public string Title { get; }

    public string Summary { get; }

    public IReadOnlyList<ApprovalEvidence> Evidence { get; }

    public IReadOnlyList<ApprovalChange> Changes { get; }

    public ApprovalActionBinding Action { get; }

    public DateTimeOffset ExpiresAt { get; }

    public ApprovalReviewContext? ReviewContext { get; }

    public string Fingerprint { get; }
}
