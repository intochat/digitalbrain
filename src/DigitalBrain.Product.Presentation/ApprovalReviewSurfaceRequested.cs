using DigitalBrain.Product.Approvals;

namespace DigitalBrain.Product.Presentation;

/// <summary>
/// A declarative, renderer-neutral representation of a frozen approval review.
/// The action binding remains inside Approvals and never crosses this boundary.
/// </summary>
public sealed record ApprovalReviewSurfaceRequested : Synapse
{
    public ApprovalReviewSurfaceRequested(
        string proposalId,
        string proposalFingerprint,
        string title,
        string summary,
        IReadOnlyList<ApprovalEvidence> evidence,
        IReadOnlyList<ApprovalChange> changes,
        DateTimeOffset expiresAt,
        ApprovalReviewContext? context,
        IReadOnlyList<ApprovalReviewDecision> decisions,
        IReadOnlyList<ApprovalReviewPlacement> placements)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(proposalId);
        ArgumentException.ThrowIfNullOrWhiteSpace(proposalFingerprint);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentNullException.ThrowIfNull(decisions);
        ArgumentNullException.ThrowIfNull(placements);
        if (expiresAt == default)
        {
            throw new ArgumentException("An approval review surface needs an expiry.", nameof(expiresAt));
        }

        var evidenceCopy = evidence.ToArray();
        var changesCopy = changes.ToArray();
        var decisionCopy = decisions.ToArray();
        var placementCopy = placements.ToArray();
        if (evidenceCopy.Any(static item => item is null))
        {
            throw new ArgumentException("Approval review evidence cannot contain null entries.", nameof(evidence));
        }

        if (changesCopy.Any(static item => item is null))
        {
            throw new ArgumentException("Approval review changes cannot contain null entries.", nameof(changes));
        }

        if (decisionCopy.Length == 0
            || decisionCopy.Any(static item => !Enum.IsDefined(item))
            || decisionCopy.Distinct().Count() != decisionCopy.Length)
        {
            throw new ArgumentException("An approval review surface needs distinct, recognized decision slots.", nameof(decisions));
        }

        if (placementCopy.Length == 0 || placementCopy.Any(static item => !Enum.IsDefined(item)))
        {
            throw new ArgumentException("An approval review surface needs recognized placement hints.", nameof(placements));
        }

        ProposalId = proposalId.Trim();
        ProposalFingerprint = proposalFingerprint.Trim();
        Title = title.Trim();
        Summary = summary.Trim();
        Evidence = Array.AsReadOnly(evidenceCopy);
        Changes = Array.AsReadOnly(changesCopy);
        ExpiresAt = expiresAt;
        Context = context;
        Decisions = Array.AsReadOnly(decisionCopy);
        Placements = Array.AsReadOnly(placementCopy);
    }

    public string ProposalId { get; }

    public string ProposalFingerprint { get; }

    public string Title { get; }

    public string Summary { get; }

    public IReadOnlyList<ApprovalEvidence> Evidence { get; }

    public IReadOnlyList<ApprovalChange> Changes { get; }

    public DateTimeOffset ExpiresAt { get; }

    public ApprovalReviewContext? Context { get; }

    public IReadOnlyList<ApprovalReviewDecision> Decisions { get; }

    public IReadOnlyList<ApprovalReviewPlacement> Placements { get; }
}
