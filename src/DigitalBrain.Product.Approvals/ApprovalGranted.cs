namespace DigitalBrain.Product.Approvals;

public sealed record ApprovalGranted(
    ApprovalProposal Proposal,
    Guid DecisionId,
    string Actor,
    DateTimeOffset DecidedAt) : Synapse;
