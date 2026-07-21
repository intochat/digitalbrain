namespace DigitalBrain.Kernel;

[Alias("db.kernel.capability-delegation-authority")]
internal interface ICapabilityDelegationAuthority : IGrain
{
    [Alias("Redeem")]
    Task RedeemAsync(CapabilityDelegation delegation);

    [Alias("Finish")]
    Task FinishAsync(CapabilityDelegation delegation, bool succeeded);
}
