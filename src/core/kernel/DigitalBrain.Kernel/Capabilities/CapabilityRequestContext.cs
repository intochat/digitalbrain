using DigitalBrain.Abstractions;

namespace DigitalBrain.Kernel;

internal static class CapabilityRequestContext
{
    private const string DeliveryKey = "db.capability-request";

    internal static SynapseDelivery? CurrentDelivery
        => RequestContext.Get(DeliveryKey) as SynapseDelivery;

    internal static Task InvokeAsync(SynapseDelivery delivery, Func<Task> invoke)
        => InvokeWithAsync(delivery, invoke);

    private static async Task InvokeWithAsync(SynapseDelivery delivery, Func<Task> invoke)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        ArgumentNullException.ThrowIfNull(invoke);

        var previous = RequestContext.Get(DeliveryKey);
        RequestContext.Set(DeliveryKey, delivery);

        try
        {
            await invoke().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }
        finally
        {
            Restore(previous);
        }
    }

    private static void Restore(object? previous)
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
