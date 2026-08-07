namespace DigitalBrain.Product.Approvals;

public sealed record ApprovalDecisionSubmitted : Synapse
{
    public ApprovalDecisionSubmitted(
        string proposalId,
        string expectedProposalFingerprint,
        Guid decisionId,
        ApprovalDecision decision)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(proposalId);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedProposalFingerprint);
        if (decisionId == Guid.Empty)
        {
            throw new ArgumentException("An approval decision needs an identity.", nameof(decisionId));
        }

        if (!Enum.IsDefined(decision))
        {
            throw new ArgumentOutOfRangeException(nameof(decision), decision, "The approval decision is not recognized.");
        }

        ProposalId = proposalId.Trim();
        ExpectedProposalFingerprint = expectedProposalFingerprint.Trim();
        DecisionId = decisionId;
        Decision = decision;
    }

    public string ProposalId { get; }

    public string ExpectedProposalFingerprint { get; }

    public Guid DecisionId { get; }

    public ApprovalDecision Decision { get; }
}
