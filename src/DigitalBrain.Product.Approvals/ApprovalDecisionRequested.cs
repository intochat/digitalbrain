namespace DigitalBrain.Product.Approvals;

public sealed record ApprovalDecisionRequested : Synapse
{
    public ApprovalDecisionRequested(
        string proposalId,
        string expectedProposalFingerprint,
        Guid decisionId,
        ApprovalDecision decision,
        string actor,
        DateTimeOffset decidedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(proposalId);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedProposalFingerprint);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        if (decisionId == Guid.Empty)
        {
            throw new ArgumentException("An approval decision needs an identity.", nameof(decisionId));
        }

        if (decidedAt == default)
        {
            throw new ArgumentException("An approval decision needs a timestamp.", nameof(decidedAt));
        }

        ProposalId = proposalId.Trim();
        ExpectedProposalFingerprint = expectedProposalFingerprint.Trim();
        DecisionId = decisionId;
        Decision = decision;
        Actor = actor.Trim();
        DecidedAt = decidedAt;
    }

    public string ProposalId { get; }

    public string ExpectedProposalFingerprint { get; }

    public Guid DecisionId { get; }

    public ApprovalDecision Decision { get; }

    public string Actor { get; }

    public DateTimeOffset DecidedAt { get; }
}
