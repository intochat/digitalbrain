namespace DigitalBrain.Kernel;

[GenerateSerializer]
[Alias("db.kernel.capability-delegation-state")]
internal sealed class CapabilityDelegationState(
    CapabilityDelegation delegation,
    CapabilityDelegationStatus status)
{
    [Id(0)]
    internal CapabilityDelegation Delegation { get; } = delegation;

    [Id(1)]
    internal CapabilityDelegationStatus Status { get; } = status;
}

[Alias("db.kernel.capability-delegation-status")]
internal enum CapabilityDelegationStatus
{
    Issued = 0,
    Consumed = 1,
    Completed = 2,
    Failed = 3,
}
