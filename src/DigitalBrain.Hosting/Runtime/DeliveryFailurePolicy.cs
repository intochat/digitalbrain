namespace DigitalBrain;

internal static class DeliveryFailurePolicy
{
    internal static bool ShouldProduceFor(string deliveredSynapseKind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deliveredSynapseKind);
        return !string.Equals(
            deliveredSynapseKind,
            SynapseKinds.NameOf(typeof(DeliveryFailed)),
            StringComparison.Ordinal);
    }
}
