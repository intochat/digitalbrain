namespace DigitalBrain.Poc.Runtime;

public enum CandidateLifecycle
{
    Draft,
    Validated,
    Quarantined,
    AwaitingOwnerApproval,
    ApprovedInactive,
    Active,
    RolledBack,
}
