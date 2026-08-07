namespace DigitalBrain.Product.Approvals;

public sealed record ApprovalRejected(
    ApprovalProposal Proposal,
    Guid DecisionId,
    string Actor,
    DateTimeOffset DecidedAt) : Synapse;
