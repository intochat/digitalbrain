namespace DigitalBrain.Product.Approvals;

public sealed record ApprovalExpired(ApprovalProposal Proposal, DateTimeOffset OccurredAt) : Synapse;
