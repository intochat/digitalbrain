namespace DigitalBrain.Product.Approvals;

/// <summary>
/// External ingress command for a caller that is allowed to submit a complete approval proposal.
/// The internally-authored <see cref="ApprovalProposed"/> fact remains unavailable to sources.
/// </summary>
public sealed record ApprovalProposalSubmitted : Synapse
{
    public ApprovalProposalSubmitted(ApprovalProposal proposal)
    {
        Proposal = proposal ?? throw new ArgumentNullException(nameof(proposal));
    }

    public ApprovalProposal Proposal { get; }
}
