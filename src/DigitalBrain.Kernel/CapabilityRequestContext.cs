using DigitalBrain.Abstractions;

namespace DigitalBrain.Kernel;

internal static class CapabilityRequestContext
{
    private const string DeliveryKey = "db.capability-request";

    internal static SynapseDelivery? Current
        => RequestContext.Get(DeliveryKey) as SynapseDelivery;

    internal static async Task InvokeAsync(SynapseDelivery delivery, Func<Task> invoke)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        ArgumentNullException.ThrowIfNull(invoke);

        var previous = RequestContext.Get(DeliveryKey);
        RequestContext.Set(DeliveryKey, delivery);

        try
        {
            await invoke();
        }
        finally
        {
            if (previous is null)
            {
                RequestContext.Remove(DeliveryKey);
            }
            else
            {
                RequestContext.Set(DeliveryKey, previous);
            }
        }
    }
}
