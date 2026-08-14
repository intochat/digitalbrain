using System.Collections.Concurrent;
using Brain.Core.Outbox;

namespace Brain.Core.Delivery;

// This is the rehydratable receiver-state seam. A production receiver persists the same
// claim before applying its state transition; this in-memory implementation is test-only.
internal interface IDeliveryReceiverState
{
    bool TryBegin(DeliveryId delivery);

    void Complete(DeliveryId delivery);

    void Abandon(DeliveryId delivery);
}

internal sealed class InMemoryDeliveryReceiverState : IDeliveryReceiverState
{
    private const byte InFlight = 0;
    private const byte Committed = 1;
    private readonly ConcurrentDictionary<DeliveryId, byte> _states = new();

    public bool TryBegin(DeliveryId delivery)
        => _states.TryAdd(delivery, InFlight);

    public void Complete(DeliveryId delivery)
    {
        if (!_states.TryUpdate(delivery, Committed, InFlight))
        {
            throw new InvalidOperationException("A delivery can only complete after it has begun.");
        }
    }

    public void Abandon(DeliveryId delivery) => _states.TryRemove(delivery, out _);
}
