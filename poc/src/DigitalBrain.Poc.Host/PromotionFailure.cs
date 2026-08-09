namespace DigitalBrain.Poc.Host;

public enum PromotionFailure
{
    None,
    CandidateNotApproved,
    CandidateVerificationFailed,
    IncompatibleRetainedSchema,
    PendingCandidateTargetedOutbox,
    ChildPreflightFailed,
    PointerHeadConflict,
    NoPreviousCandidate,
    ActivationFailed,
    ActivationRecoveryFailed,
    HostAuthorityUnavailable,
}
