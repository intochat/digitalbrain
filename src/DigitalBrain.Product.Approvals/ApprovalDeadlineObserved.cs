namespace DigitalBrain.Product.Approvals;

public sealed record ApprovalDeadlineObserved : Synapse
{
    public ApprovalDeadlineObserved(
        string proposalId,
        string expectedProposalFingerprint,
        DateTimeOffset occurredAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(proposalId);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedProposalFingerprint);
        if (occurredAt == default)
        {
            throw new ArgumentException("An approval deadline needs a timestamp.", nameof(occurredAt));
        }

        ProposalId = proposalId.Trim();
        ExpectedProposalFingerprint = expectedProposalFingerprint.Trim();
        OccurredAt = occurredAt;
    }

    public string ProposalId { get; }

    public string ExpectedProposalFingerprint { get; }

    public DateTimeOffset OccurredAt { get; }
}
