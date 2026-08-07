using DigitalBrain.Abstractions;

namespace DigitalBrain.Core;

public abstract partial class Neuron
{
    protected void ValidateCapabilityCaller(NeuronId expectedCaller)
        => _ = CurrentCapabilityRequestFrom(expectedCaller);

    protected void EnlistTurnRollback(Action rollback)
    {
        ArgumentNullException.ThrowIfNull(rollback);

        if (_handling is null || _turnCheckpoint is null)
        {
            throw new InvalidOperationException(
                $"Neuron '{Id}' can enlist rollback only while handling a durable turn.");
        }

        _turnRollbacks.Add(rollback);
    }

    private SynapseDelivery CurrentCapabilityRequestFrom(NeuronId expectedCaller)
    {
        if (expectedCaller == default)
        {
            throw new ArgumentException("A capability causation caller is required.", nameof(expectedCaller));
        }

        var delivery = _handling
            ?? throw new InvalidOperationException(
                $"Neuron '{Id}' can validate a capability caller only while handling a committed capability request.");

        if (_turnCheckpoint is not { InboundCommitted: true })
        {
            throw new InvalidOperationException(
                $"Neuron '{Id}' can validate a capability caller only after its incoming capability request has been committed.");
        }

        if (delivery.Synapse is not CapabilityRequested request || request.Target != Id)
        {
            throw new InvalidOperationException(
                $"Neuron '{Id}' can validate only a committed capability request targeting itself.");
        }

        if (delivery.Caller != expectedCaller)
        {
            throw new NeuronAuthorizationException(
                $"Capability request '{delivery.SynapseId}' was sent by '{delivery.Caller}', not expected caller '{expectedCaller}'.");
        }

        return delivery;
    }

    internal async Task<SynapseDelivery> BeginCapabilityRequestAsync(string contract, string method, NeuronId target)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contract);
        ArgumentException.ThrowIfNullOrWhiteSpace(method);

        var sequence = _outgoing.NextSequence
            + (_handling is null ? 0 : _firedWhileHandling.Count);
        var delivery = SynapseDelivery.Create(
            new CapabilityRequested(contract, method, target),
            Id,
            sequence,
            _handling,
            TimeProvider);

        StageInboundCause();
        FlushOutgoing();
        _outgoing.Append(delivery);
        await CommitAsync(CancellationToken.None).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        AdvanceTurnCheckpoint();
        await NotifyWatchersAsync().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        return delivery;
    }

    internal bool TryRegisterStreamedCapabilityRequest(Guid enumerationId, SynapseDelivery request)
        => _streamedCapabilityRequests.TryAdd(enumerationId, request);

    internal bool TryClaimStreamedCapabilityRequest(Guid enumerationId, out SynapseDelivery request)
        => _streamedCapabilityRequests.TryRemove(enumerationId, out request!);

    internal int PendingStreamedCapabilityRequests => _streamedCapabilityRequests.Count;

    internal async Task RecordCapabilityOutcomeAsync(CapabilityOutcome outcome, SynapseDelivery request)
    {
        ArgumentNullException.ThrowIfNull(request);

        Synapse fact = outcome switch
        {
            CapabilityOutcome.Completed => new CapabilityCompleted(request.SynapseId),
            CapabilityOutcome.Failed => new CapabilityFailed(request.SynapseId),
            CapabilityOutcome.Rejected => new CapabilityRejected(request.SynapseId),
            CapabilityOutcome.Abandoned => new CapabilityAbandoned(request.SynapseId),
            _ => throw new ArgumentOutOfRangeException(nameof(outcome)),
        };

        var sequence = _outgoing.NextSequence + _firedWhileHandling.Count;
        var delivery = SynapseDelivery.Create(fact, Id, sequence, request, TimeProvider);

        FlushOutgoing();
        _outgoing.Append(delivery);
        await CommitAsync(CancellationToken.None).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        AdvanceTurnCheckpoint();
        await NotifyWatchersAsync().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }
}
