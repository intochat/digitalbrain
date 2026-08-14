using System.Collections.Immutable;
using Brain.Abstractions.Events;
using Brain.Core.Outbox;

namespace Brain.Core.Delivery;

// Receivers own this transaction boundary. Production neuron persistence must atomically
// publish the receipt together with the next receiver state, journal, and outbox. This
// in-memory implementation is a deterministic copy-on-write proof seam, not a distributed
// durability claim.
/// <summary>
/// Stages a receiver's next immutable aggregate for one delivery. <typeparamref name="TState"/>
/// includes every receiver-owned durable concern that must move with a receipt (for example,
/// neuron state, its journal, and any next outbox entries). Implementations must return a
/// candidate only; they must not publish externally visible effects while staging.
/// </summary>
internal interface IReceiverDeliveryHandler<TState>
{
    Task<TState> StageAsync(
        TState candidate,
        DeliverySnapshot snapshot,
        IDomainEvent domainEvent,
        CancellationToken cancellationToken);
}

/// <summary>
/// Owns the receiver transaction: a successful call commits both the staged aggregate and the
/// delivery receipt together, while a failed call commits neither.
/// </summary>
internal interface IReceiverDeliveryStore<TState>
{
    Task<ReceiverDeliveryResult> DeliverAsync(
        DeliverySnapshot snapshot,
        IDomainEvent domainEvent,
        IReceiverDeliveryHandler<TState> handler,
        CancellationToken cancellationToken);
}

internal readonly record struct ReceiverDeliveryResult(bool Applied);

internal sealed record ReceiverDeliveryState<TState>(
    TState State,
    ImmutableHashSet<DeliveryId> Receipts);

internal sealed class InMemoryReceiverDeliveryStore<TState>(TState initialState)
    : IReceiverDeliveryStore<TState>
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ReceiverDeliveryState<TState> _current = new(initialState, ImmutableHashSet<DeliveryId>.Empty);

    internal TState State => _current.State;

    internal int CompletedReceiptCount => _current.Receipts.Count;

    public async Task<ReceiverDeliveryResult> DeliverAsync(
        DeliverySnapshot snapshot,
        IDomainEvent domainEvent,
        IReceiverDeliveryHandler<TState> handler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        ArgumentNullException.ThrowIfNull(handler);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var observed = _current;
            if (observed.Receipts.Contains(snapshot.Delivery))
            {
                return new ReceiverDeliveryResult(false);
            }

            var nextState = await handler.StageAsync(
                observed.State,
                snapshot,
                domainEvent,
                cancellationToken).ConfigureAwait(false);
            _current = new ReceiverDeliveryState<TState>(
                nextState,
                observed.Receipts.Add(snapshot.Delivery));
            return new ReceiverDeliveryResult(true);
        }
        finally
        {
            _gate.Release();
        }
    }
}
