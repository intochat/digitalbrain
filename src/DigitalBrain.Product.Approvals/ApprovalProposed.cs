namespace DigitalBrain.Product.Approvals;

public sealed record ApprovalProposed : Synapse
{
    public ApprovalProposed(ApprovalProposal proposal)
    {
        Proposal = proposal ?? throw new ArgumentNullException(nameof(proposal));
    }

    public ApprovalProposal Proposal { get; }
}
