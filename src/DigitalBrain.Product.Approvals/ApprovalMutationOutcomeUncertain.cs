namespace DigitalBrain.Product.Approvals;

/// <summary>
/// A provider-neutral terminal execution outcome for an already-approved proposal.
/// </summary>
public sealed record ApprovalMutationOutcomeUncertain : Synapse
{
    public ApprovalMutationOutcomeUncertain(string proposalId, string proposalFingerprint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(proposalId);
        ArgumentException.ThrowIfNullOrWhiteSpace(proposalFingerprint);
        ProposalId = proposalId.Trim();
        ProposalFingerprint = proposalFingerprint.Trim();
    }

    public string ProposalId { get; }

    public string ProposalFingerprint { get; }
}
