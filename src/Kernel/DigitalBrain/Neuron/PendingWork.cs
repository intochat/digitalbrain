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
    private readonly HashSet<SignalId> _queuedIds = [];

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

    internal void NoteReloaded()
    {
        _queuedIds.Clear();
        foreach (var delivery in _queue)
        {
            _queuedIds.Add(delivery.SignalId);
        }
    }

    internal bool HasRoomFor(int staged) => Count + staged < MaxPending;

    internal DeliveryAdmission Classify(SignalDelivery delivery)
    {
        if (_queuedIds.Contains(delivery.SignalId) || _reacted.Contains(delivery.SignalId))
        {
            return DeliveryAdmission.Duplicate;
        }

        if (!HasRoomFor(0))
        {
            return DeliveryAdmission.Busy;
        }

        return DeliveryAdmission.Accepted;
    }

    internal void Admit(SignalDelivery delivery)
    {
        _queue.Enqueue(delivery);
        _queuedIds.Add(delivery.SignalId);
    }

    internal HeadToken? Peek() => _queue.TryPeek(out var delivery) ? new HeadToken(delivery) : null;

    internal void CompleteHead(HeadToken token)
    {
        var id = token.Delivery.SignalId;
        if (!_queue.TryPeek(out var head) || head.SignalId != id)
        {
            throw new InvalidOperationException("The drain completes only the head it peeked.");
        }

        _queue.Dequeue();
        _queuedIds.Remove(id);
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
        if (!_queuedIds.Contains(id))
        {
            return false;
        }

        _cancelled.Add(id);
        return true;
    }

    internal bool IsCancelled(SignalId id) => _cancelled.Contains(id);

    internal readonly record struct HeadToken(SignalDelivery Delivery);
}
