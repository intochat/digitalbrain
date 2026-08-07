namespace DigitalBrain.Product.Approvals;

public enum ApprovalDecisionIgnoreReason
{
    ProposalIdentityMismatch,
    UntrustedControlOrigin,
    ProposalMissing,
    ProposalAlreadyRecorded,
    FingerprintMismatch,
    AlreadyFinalized,
    DeadlineNotReached,
    Expired,
    InvalidDecision,
}
