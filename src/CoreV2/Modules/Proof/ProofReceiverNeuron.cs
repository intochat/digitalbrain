using Brain.Abstractions.Contracts;
using Brain.Abstractions.Events;
using Brain.Core.Delivery;
using Brain.Core.Endpoints;
using Brain.Core.Outbox;
using Brain.Modules.Proof.Contracts;

namespace Brain.Modules.Proof;

internal sealed class ProofReceiverNeuron : IDeliveryReceiver
{
    private readonly InMemoryReceiverDeliveryStore<ProofResult?> _store = new(null);
    private readonly Func<DeliverySnapshot, ProofResult, Task> _complete;

    internal ProofReceiverNeuron(EndpointAddress endpoint, ContractId acceptedContract, string route, Func<DeliverySnapshot, ProofResult, Task> complete)
    {
        Endpoint = endpoint;
        AcceptedContract = acceptedContract;
        Route = route;
        _complete = complete ?? throw new ArgumentNullException(nameof(complete));
    }

    public EndpointAddress Endpoint { get; }

    public ContractId AcceptedContract { get; }

    internal string Route { get; }

    internal int ReceiptCount => _store.CompletedReceiptCount;

    public async Task<ReceiverDeliveryResult> DeliverAsync(
        DeliverySnapshot snapshot,
        IDomainEvent domainEvent,
        CancellationToken cancellationToken)
    {
        var result = new ProofResult(Route);
        var applied = await _store.DeliverAsync(snapshot, domainEvent, new Handler(result), cancellationToken);
        if (applied.Applied)
        {
            await _complete(snapshot, result);
        }

        return applied;
    }

    private sealed class Handler(ProofResult result) : IReceiverDeliveryHandler<ProofResult?>
    {
        public Task<ProofResult?> StageAsync(
            ProofResult? candidate,
            DeliverySnapshot snapshot,
            IDomainEvent domainEvent,
            CancellationToken cancellationToken)
            => Task.FromResult<ProofResult?>(result);
    }
}
