namespace DigitalBrain.Product.Approvals;

public sealed record ApprovalDeadlineElapsed : Synapse
{
    public ApprovalDeadlineElapsed(
        string proposalId,
        string expectedProposalFingerprint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(proposalId);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedProposalFingerprint);
        ProposalId = proposalId.Trim();
        ExpectedProposalFingerprint = expectedProposalFingerprint.Trim();
    }

    public string ProposalId { get; }

    public string ExpectedProposalFingerprint { get; }

}
