using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Signals;
using Orleans.Journaling;

namespace DigitalBrain.Core;

internal sealed class PendingWork
{
    internal const int MaxPending = 256;
    internal const int MaxReactedIds = 256;

    private readonly IDurableQueue<SignalDelivery> _queue;
    private readonly IDurableSet<SignalId> _cancelled;
    private readonly IDurableList<SignalId> _reacted;

    internal PendingWork(IDurableQueue<SignalDelivery> queue, IDurableSet<SignalId> cancelled, IDurableList<SignalId> reacted)
    {
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(cancelled);
        ArgumentNullException.ThrowIfNull(reacted);
        _queue = queue;
        _cancelled = cancelled;
        _reacted = reacted;
    }

    internal int Count => _queue.Count;

    internal DeliveryAdmission TryAdmit(SignalDelivery delivery)
    {
        if (_queue.Any(entry => entry.SignalId == delivery.SignalId) || _reacted.Contains(delivery.SignalId))
        {
            return DeliveryAdmission.Duplicate;
        }

        if (Count >= MaxPending)
        {
            return DeliveryAdmission.Busy;
        }

        _queue.Enqueue(delivery);
        return DeliveryAdmission.Accepted;
    }

    internal SignalDelivery? Peek() => _queue.TryPeek(out var delivery) ? delivery : null;

    internal void CompleteHead(SignalId id)
    {
        if (!_queue.TryPeek(out var head) || head.SignalId != id)
        {
            throw new InvalidOperationException(
                $"Cannot complete signal '{id}' when the head is '{head?.SignalId.ToString() ?? "<empty>"}'; the drain completes only the head it peeked.");
        }

        _queue.Dequeue();
        _cancelled.Remove(id);
        // Cancelled work is finished too; remembering its id prevents admission on redelivery.
        _reacted.Add(id);
        while (_reacted.Count > MaxReactedIds)
        {
            _reacted.RemoveAt(0);
        }
    }

    internal bool Cancel(SignalId id)
    {
        if (!_queue.Any(entry => entry.SignalId == id))
        {
            return false;
        }

        _cancelled.Add(id);
        return true;
    }

    internal bool IsCancelled(SignalId id) => _cancelled.Contains(id);
}
