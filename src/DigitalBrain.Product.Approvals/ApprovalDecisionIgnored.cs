namespace DigitalBrain.Product.Approvals;

public sealed record ApprovalDecisionIgnored(
    string ProposalId,
    Guid? DecisionId,
    ApprovalDecisionIgnoreReason Reason) : Synapse;
