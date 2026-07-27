namespace DigitalBrain.Kernel;

[GenerateSerializer]
[Alias("db.kernel.redeemed-capability-delegation")]
internal sealed class RedeemedCapabilityDelegation(CapabilityDelegation delegation)
{
    [Id(0)]
    internal CapabilityDelegation Delegation { get; } = delegation;
}
