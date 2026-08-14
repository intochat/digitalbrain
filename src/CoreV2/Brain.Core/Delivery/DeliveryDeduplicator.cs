using Brain.Core.Outbox;

namespace Brain.Core.Delivery;

// A receiver owns this boundary so its state effect, journal/outbox work, and committed
// delivery marker can share one transaction in production. The in-memory version serializes
// the full receiver application and records the marker only after the application succeeds.
internal interface IDeliveryReceiverTransaction
{
    Task<ReceiverDeliveryResult> ApplyOnceAsync(
        DeliveryId delivery,
        Func<CancellationToken, Task> application,
        CancellationToken cancellationToken);
}

internal readonly record struct ReceiverDeliveryResult(bool Applied);

internal sealed class InMemoryDeliveryReceiverTransaction : IDeliveryReceiverTransaction
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly HashSet<DeliveryId> _committed = [];

    public async Task<ReceiverDeliveryResult> ApplyOnceAsync(
        DeliveryId delivery,
        Func<CancellationToken, Task> application,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_committed.Contains(delivery))
            {
                return new ReceiverDeliveryResult(false);
            }

            await application(cancellationToken).ConfigureAwait(false);
            _committed.Add(delivery);
            return new ReceiverDeliveryResult(true);
        }
        finally
        {
            _gate.Release();
        }
    }
}
