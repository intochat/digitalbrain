using DigitalBrain.Abstractions;

namespace DigitalBrain.Kernel;

internal static class CapabilityRequestContext
{
    private const string DeliveryKey = "db.capability-request";
    private static readonly AsyncLocal<CapabilityDelegation?> LocalDelegation = new();

    internal static SynapseDelivery? CurrentDelivery
        => RequestContext.Get(DeliveryKey) as SynapseDelivery;

    internal static CapabilityDelegation? CurrentDelegation
        => LocalDelegation.Value;

    internal static RedeemedCapabilityDelegation? CurrentRedeemedDelegation
        => RequestContext.Get(DeliveryKey) as RedeemedCapabilityDelegation;

    internal static Task InvokeAsync(SynapseDelivery delivery, Func<Task> invoke)
        => InvokeWithAsync(delivery, invoke);

    internal static Task<TResult> InvokeAsync<TResult>(
        CapabilityDelegation delegation,
        Func<Task<TResult>> invoke)
        => InvokeWithLocalDelegationAsync(delegation, invoke);

    internal static Task InvokeRedeemedAsync(
        CapabilityDelegation delegation,
        Func<Task> invoke)
        => InvokeWithAsync(new RedeemedCapabilityDelegation(delegation), invoke);

    private static async Task<TResult> InvokeWithLocalDelegationAsync<TResult>(
        CapabilityDelegation delegation,
        Func<Task<TResult>> invoke)
    {
        ArgumentNullException.ThrowIfNull(delegation);
        ArgumentNullException.ThrowIfNull(invoke);

        var previous = LocalDelegation.Value;
        LocalDelegation.Value = delegation;

        try
        {
            return await invoke();
        }
        finally
        {
            LocalDelegation.Value = previous;
        }
    }

    private static async Task InvokeWithAsync(object carried, Func<Task> invoke)
    {
        ArgumentNullException.ThrowIfNull(carried);
        ArgumentNullException.ThrowIfNull(invoke);

        var previous = RequestContext.Get(DeliveryKey);
        RequestContext.Set(DeliveryKey, carried);

        try
        {
            await invoke();
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
