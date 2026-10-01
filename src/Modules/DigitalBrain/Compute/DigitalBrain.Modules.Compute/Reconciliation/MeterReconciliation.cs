namespace DigitalBrain.Compute.Reconciliation;

public sealed record ReconciliationReport(
    decimal MeteredQuantity,
    decimal ProviderQuantity,
    decimal DeltaPercent,
    bool WithinTolerance);

public static class MeterReconciliation
{
    public const decimal DefaultTolerancePercent = 1m;

    public static ReconciliationReport Compare(
        decimal meteredQuantity, decimal providerQuantity, decimal tolerancePercent = DefaultTolerancePercent)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(tolerancePercent);
        var delta = providerQuantity == 0m
            ? meteredQuantity == 0m ? 0m : 100m
            : (meteredQuantity - providerQuantity) / providerQuantity * 100m;
        return new(meteredQuantity, providerQuantity, delta, Math.Abs(delta) <= tolerancePercent);
    }
}
