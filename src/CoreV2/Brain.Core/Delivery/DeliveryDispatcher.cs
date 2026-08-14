using System.Collections.Concurrent;
using Brain.Abstractions.Contracts;
using Brain.Abstractions.Events;
using Brain.Core.Endpoints;
using Brain.Core.Outbox;
using Brain.Core.Reshapes;

namespace Brain.Core.Delivery;

internal interface IFiringPayloadStore
{
    void Record<TEvent>(FiringId firing, ContractId contract, TEvent domainEvent)
        where TEvent : IDomainEvent;

    IDomainEvent Read(FiringId firing, ContractId contract);
}

internal sealed class InMemoryFiringPayloadStore : IFiringPayloadStore
{
    private readonly ConcurrentDictionary<FiringPayloadKey, IDomainEvent> _payloads = new();

    public void Record<TEvent>(FiringId firing, ContractId contract, TEvent domainEvent)
        where TEvent : IDomainEvent
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        RuntimeRecordValidation.Contract(contract, nameof(contract));
        if (!_payloads.TryAdd(new FiringPayloadKey(firing, contract), domainEvent))
        {
            throw new InvalidOperationException("A firing payload is immutable once recorded for its declared contract.");
        }
    }

    public IDomainEvent Read(FiringId firing, ContractId contract)
    {
        RuntimeRecordValidation.Contract(contract, nameof(contract));
        return _payloads.TryGetValue(new FiringPayloadKey(firing, contract), out var payload)
            ? payload
            : throw new KeyNotFoundException("No firing payload exists for the declared contract.");
    }

    private readonly record struct FiringPayloadKey(FiringId Firing, ContractId Contract);
}

internal interface IDeliveryReceiverDirectory
{
    IDeliveryReceiver Resolve(EndpointAddress target);
}

internal interface IDeliveryReceiver
{
    EndpointAddress Endpoint { get; }

    ContractId AcceptedContract { get; }

    IDeliveryReceiverState DeliveryState { get; }

    Task ApplyAsync(DeliverySnapshot snapshot, IDomainEvent domainEvent, CancellationToken cancellationToken);
}

internal readonly record struct DeliveryDispatchResult(int DeliveredCount, int DuplicateCount)
{
    internal bool CreatedRefusal => false;
}

internal sealed class DeliveryDispatcher(
    IFiringPayloadStore payloads,
    IDeliveryReceiverDirectory receivers,
    IReshapeRegistry reshapes)
{
    private readonly IFiringPayloadStore _payloads = payloads ?? throw new ArgumentNullException(nameof(payloads));
    private readonly IDeliveryReceiverDirectory _receivers = receivers ?? throw new ArgumentNullException(nameof(receivers));
    private readonly IReshapeRegistry _reshapes = reshapes ?? throw new ArgumentNullException(nameof(reshapes));

    internal async Task<DeliveryDispatchResult> DispatchAsync(OutboxEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var delivered = 0;
        var duplicates = 0;
        foreach (var snapshot in entry.Deliveries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await DispatchAsync(entry, snapshot, cancellationToken).ConfigureAwait(false);
            delivered += result.DeliveredCount;
            duplicates += result.DuplicateCount;
        }

        return new DeliveryDispatchResult(delivered, duplicates);
    }

    private async Task<DeliveryDispatchResult> DispatchAsync(
        OutboxEntry entry,
        DeliverySnapshot snapshot,
        CancellationToken cancellationToken)
    {
        snapshot.EnsureValid();
        if (snapshot.InputContract != entry.EventContract)
        {
            throw new InvalidOperationException("A delivery snapshot input contract must match its firing contract.");
        }

        var receiver = _receivers.Resolve(snapshot.Target);
        if (receiver.Endpoint != snapshot.Target || receiver.AcceptedContract != snapshot.OutputContract)
        {
            throw new InvalidOperationException("A delivery receiver must accept the exact snapshot target and output contract.");
        }

        var payload = _payloads.Read(entry.Firing, snapshot.InputContract);
        if (snapshot.Reshape is { })
        {
            _reshapes.Validate(snapshot, payload);
        }
        else if (snapshot.OutputContract != snapshot.InputContract)
        {
            throw new InvalidOperationException("A delivery without a reshape must preserve its input contract.");
        }

        if (!receiver.DeliveryState.TryBegin(snapshot.Delivery))
        {
            return new DeliveryDispatchResult(0, 1);
        }

        try
        {
            var delivered = snapshot.Reshape is { }
                ? _reshapes.Transform(snapshot, payload)
                : payload;
            await receiver.ApplyAsync(snapshot, delivered, cancellationToken).ConfigureAwait(false);
            receiver.DeliveryState.Complete(snapshot.Delivery);
            return new DeliveryDispatchResult(1, 0);
        }
        catch
        {
            // Receiver application and delivery marker must commit together. The in-memory
            // seam has no partial receiver effect on failure, so its reservation is released.
            receiver.DeliveryState.Abandon(snapshot.Delivery);
            throw;
        }
    }
}
