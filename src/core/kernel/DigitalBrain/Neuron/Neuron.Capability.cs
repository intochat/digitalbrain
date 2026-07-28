using DigitalBrain.Abstractions;

namespace DigitalBrain.Kernel;

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

    protected Task<CapabilityDelegation> DelegateCapabilityAsync(GrainId delegateSource, NeuronId target, Type contract, string method)
        => DelegateCapabilityAsync(_handling, delegateSource, target, contract, method);

    private async Task<CapabilityDelegation> DelegateCapabilityAsync(
        SynapseDelivery? causation,
        GrainId delegateSource,
        NeuronId target,
        Type contract,
        string method)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentException.ThrowIfNullOrWhiteSpace(method);

        if (!contract.IsInterface || contract.FullName is null)
        {
            throw new ArgumentException("The delegated capability contract must be a named interface.", nameof(contract));
        }

        var matchingMethods = contract.GetMethods()
            .Where(candidate => candidate.Name == method)
            .ToArray();

        if (matchingMethods.Length != 1)
        {
            throw new ArgumentException(
                $"Capability contract '{contract.FullName}' must have exactly one method named '{method}'.",
                nameof(method));
        }

        if (GrainOwnership.RequireOwner(delegateSource) != Id.Owner || target.Owner != Id.Owner)
        {
            throw new NeuronAuthorizationException(
                $"Neuron '{Id}' can delegate only to a runner and target owned by '{Id.Owner}'.");
        }

        var sequence = _outgoing.NextSequence
            + (_handling is null ? 0 : _firedWhileHandling.Count);
        var request = SynapseDelivery.Create(
            new CapabilityRequested(contract.FullName, method, target),
            Id,
            sequence,
            causation,
            TimeProvider);
        var delegation = new CapabilityDelegation(Guid.NewGuid(), request, delegateSource, Id.Owner);
        var delegationCheckpoint = SnapshotDelegations();
        var outgoingCheckpoint = _outgoing.Checkpoint();

        try
        {
            MakeRoomForDelegation();
            StageInboundCause();
            FlushOutgoing();
            _outgoing.Append(request);
            _delegations.Add(delegation.Identity, _delegationStates.SerializeToArray(new(delegation, CapabilityDelegationStatus.Issued)));
            await CommitAsync(CancellationToken.None);
        }
        catch
        {
            RestoreDelegations(delegationCheckpoint);
            _outgoing.Restore(outgoingCheckpoint);

            throw;
        }

        AdvanceTurnCheckpoint();
        await NotifyWatchersAsync();

        return delegation;
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
        await CommitAsync(CancellationToken.None);
        AdvanceTurnCheckpoint();
        await NotifyWatchersAsync();

        return delivery;
    }

    internal void RegisterStreamedCapabilityRequest(Guid enumerationId, SynapseDelivery request)
        => _streamedCapabilityRequests[enumerationId] = request;

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
        await CommitAsync(CancellationToken.None);
        AdvanceTurnCheckpoint();
        await NotifyWatchersAsync();
    }
}
