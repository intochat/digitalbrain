using System.Diagnostics;
using DigitalBrain.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.Serialization;

namespace DigitalBrain.Kernel;

public abstract partial class Neuron
{
    protected SynapseId CaptureCapabilityCausation(NeuronId expectedCaller)
    {
        var delivery = CurrentCapabilityRequestFrom(expectedCaller);

        if (_capturedCapabilityCauses.ContainsKey(delivery.SynapseId.Value))
        {
            return delivery.SynapseId;
        }

        if (_capturedCapabilityCauses.Count >= MaximumCapturedCapabilityCauses)
        {
            throw new InvalidOperationException(
                $"Neuron '{Id}' has reached its limit of {MaximumCapturedCapabilityCauses} unresolved captured capability causes. Complete a deferred reply before capturing another request.");
        }

        _capturedCapabilityCauses.Add(
            delivery.SynapseId.Value,
            _deliveries.SerializeToArray(delivery));

        return delivery.SynapseId;
    }

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

    protected async Task ReplyAsync(SynapseId causation, Synapse synapse)
    {
        ArgumentNullException.ThrowIfNull(synapse);

        var answered = RequireCapabilityCausation(causation);

        if (_handling is not null)
        {
            _capturedCapabilityCauses.Remove(causation.Value);
            await FireAsync(synapse, [answered.Caller], answered);

            return;
        }

        var turn = BeginCapturedCapabilityReply(answered);

        try
        {
            _capturedCapabilityCauses.Remove(causation.Value);
            await FireAsync(synapse, [answered.Caller], answered);
            await CompleteIncomingCapabilityRequestAsync(turn);
        }
        catch
        {
            FailIncomingCapabilityRequest(turn);

            throw;
        }
    }

    protected Task<CapabilityDelegation> DelegateCapabilityAsync(
        GrainId delegateSource,
        NeuronId target,
        Type contract,
        string method)
        => DelegateCapabilityAsync(_handling, delegateSource, target, contract, method);

    protected Task<CapabilityDelegation> DelegateCapabilityAsync(
        SynapseId causation,
        GrainId delegateSource,
        NeuronId target,
        Type contract,
        string method)
        => DelegateCapabilityAsync(
            RequireCapabilityCausation(causation),
            delegateSource,
            target,
            contract,
            method);

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
        var delegation = new CapabilityDelegation(
            Guid.NewGuid(),
            request,
            delegateSource,
            Id.Owner);
        var delegationCheckpoint = SnapshotDelegations();
        var outgoingCheckpoint = _outgoing.Checkpoint();

        try
        {
            MakeRoomForDelegation();
            StageInboundCause();
            FlushOutgoing();
            _outgoing.Append(request);
            _delegations.Add(
                delegation.Identity,
                _delegationStates.SerializeToArray(new(
                    delegation,
                    CapabilityDelegationStatus.Issued)));
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

    private SynapseDelivery RequireCapabilityCausation(SynapseId causation)
    {
        var delivery = _capturedCapabilityCauses.TryGetValue(causation.Value, out var serialized)
            ? _deliveries.Deserialize(serialized)
            ?? throw new InvalidOperationException(
                $"Neuron '{Id}' has no captured committed incoming capability request '{causation}'.")
            : throw new InvalidOperationException(
                $"Neuron '{Id}' has no captured committed incoming capability request '{causation}'.");

        if (delivery.Synapse is not CapabilityRequested request || request.Target != Id)
        {
            throw new InvalidOperationException(
                $"Incoming delivery '{causation}' is not a committed capability request targeting neuron '{Id}'.");
        }

        return delivery;
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

    internal async Task<SynapseDelivery> BeginCapabilityRequestAsync(
        string contract,
        string method,
        NeuronId target)
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

    internal async Task RecordCapabilityOutcomeAsync(
        CapabilityOutcome outcome,
        SynapseDelivery request)
    {
        ArgumentNullException.ThrowIfNull(request);

        Synapse fact = outcome switch
        {
            CapabilityOutcome.Completed => new CapabilityCompleted(request.SynapseId),
            CapabilityOutcome.Failed => new CapabilityFailed(request.SynapseId),
            CapabilityOutcome.Rejected => new CapabilityRejected(request.SynapseId),
            _ => throw new ArgumentOutOfRangeException(
                nameof(outcome)),
        };

        var sequence = _outgoing.NextSequence + _firedWhileHandling.Count;
        var delivery = SynapseDelivery.Create(
            fact,
            Id,
            sequence,
            request,
            TimeProvider);

        FlushOutgoing();
        _outgoing.Append(delivery);
        await CommitAsync(CancellationToken.None);
        AdvanceTurnCheckpoint();
        await NotifyWatchersAsync();
    }

    async Task ICapabilityDelegationAuthority.RedeemAsync(CapabilityDelegation delegation)
    {
        ArgumentNullException.ThrowIfNull(delegation);

        if (!_delegations.TryGetValue(delegation.Identity, out var serialized))
        {
            throw new NeuronAuthorizationException("The capability delegation was not issued by its causal caller.");
        }

        var state = _delegationStates.Deserialize(serialized);

        if (!state.Delegation.Matches(delegation)
            || delegation.Request.Caller != Id
            || delegation.Owner != Id.Owner)
        {
            throw new NeuronAuthorizationException("The capability delegation does not match its durable issued state.");
        }

        if (state.Status != CapabilityDelegationStatus.Issued)
        {
            throw new NeuronAuthorizationException("The capability delegation has already been consumed.");
        }

        var delegationCheckpoint = SnapshotDelegations();

        try
        {
            _delegations[delegation.Identity] = _delegationStates.SerializeToArray(new(
                state.Delegation,
                CapabilityDelegationStatus.Consumed));
            _delegationConsumed.Add(delegation.Identity);
            await CommitAsync(CancellationToken.None);
        }
        catch
        {
            RestoreDelegations(delegationCheckpoint);

            throw;
        }

        AdvanceTurnCheckpoint();
    }

    async Task ICapabilityDelegationAuthority.FinishAsync(CapabilityDelegation delegation, bool succeeded)
    {
        ArgumentNullException.ThrowIfNull(delegation);

        if (!_delegations.TryGetValue(delegation.Identity, out var serialized))
        {
            throw new NeuronAuthorizationException("The capability delegation was not issued by its causal caller.");
        }

        var state = _delegationStates.Deserialize(serialized);

        if (!state.Delegation.Matches(delegation))
        {
            throw new NeuronAuthorizationException("The capability delegation is not awaiting an outcome.");
        }

        var terminal = succeeded ? CapabilityDelegationStatus.Completed : CapabilityDelegationStatus.Failed;

        if (state.Status == terminal)
        {
            return;
        }

        if (state.Status != CapabilityDelegationStatus.Consumed)
        {
            throw new NeuronAuthorizationException(
                "The capability delegation already has a contradictory terminal outcome.");
        }

        var consumedIndex = IndexOf(_delegationConsumed, delegation.Identity);

        if (consumedIndex < 0)
        {
            throw new InvalidOperationException(
                "The durable capability delegation state is missing its consumed retention entry.");
        }

        var fact = succeeded
            ? (Synapse)new CapabilityCompleted(delegation.Request.SynapseId)
            : new CapabilityFailed(delegation.Request.SynapseId);
        var sequence = _outgoing.NextSequence + _firedWhileHandling.Count;
        var delivery = SynapseDelivery.Create(
            fact,
            Id,
            sequence,
            delegation.Request,
            TimeProvider);
        var delegationCheckpoint = SnapshotDelegations();
        var outgoingCheckpoint = _outgoing.Checkpoint();

        try
        {
            FlushOutgoing();
            _outgoing.Append(delivery);
            _delegations[delegation.Identity] = _delegationStates.SerializeToArray(new(
                state.Delegation,
                terminal));
            _delegationConsumed.RemoveAt(consumedIndex);
            _delegationTerminals.Add(delegation.Identity);
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
    }

    private CapabilityTurn BeginCapturedCapabilityReply(SynapseDelivery answered)
    {
        var turn = new CapabilityTurn(
            _outbox.Count,
            SnapshotCapturedCapabilityCauses(),
            _outgoing.Checkpoint(),
            [.. _turnRollbacks],
            _handling,
            _handlingDepth,
            _turnCheckpoint);

        _handling = answered;
        _handlingDepth = DeliveryPolicy.InboundDepth();
        _turnCheckpoint = new(
            _outbox.Count,
            _handled.Count,
            InboundCommitted: true,
            SnapshotCapturedCapabilityCauses(),
            _incoming.Checkpoint(),
            _outgoing.Checkpoint());
        _firedWhileHandling.Clear();
        _turnRollbacks.Clear();

        return turn;
    }

    internal async Task<CapabilityTurn> BeginIncomingCapabilityRequestAsync(
        SynapseDelivery delivery,
        GrainId? source,
        GrainId? delegatedSource = null)
    {
        ArgumentNullException.ThrowIfNull(delivery);

        if (_handling is not null)
        {
            throw new InvalidOperationException(
                $"Neuron '{Id}' cannot begin a capability request while it is already handling '{_handling.SynapseId}'.");
        }

        if (delivery.Synapse is not CapabilityRequested request || request.Target != Id)
        {
            throw new InvalidOperationException(
                $"The capability request delivery does not target neuron '{Id}'.");
        }

        var sourceMatches = delegatedSource is { } expectedDelegate
            ? source == expectedDelegate
            : source is not null
                && NeuronId.FromGrainKey(
                    source.Value.Type.ToString()
                        ?? throw new InvalidOperationException("The capability caller has no grain type."),
                    source.Value.Key.ToString()) == delivery.Caller;

        if (!sourceMatches)
        {
            throw new NeuronAuthorizationException(
                $"The capability request caller '{delivery.Caller}' does not authorize its actual Orleans source.");
        }

        var turn = new CapabilityTurn(
            _outbox.Count,
            SnapshotCapturedCapabilityCauses(),
            _outgoing.Checkpoint(),
            [.. _turnRollbacks],
            _handling,
            _handlingDepth,
            _turnCheckpoint);

        var incomingCheckpoint = _incoming.Checkpoint();

        try
        {
            _incoming.Append(delivery);
            await CommitAsync(CancellationToken.None);
        }
        catch
        {
            _incoming.Restore(incomingCheckpoint);
            throw;
        }

        await NotifyWatchersAsync();

        _handling = delivery;
        _handlingDepth = DeliveryPolicy.InboundDepth();
        _turnCheckpoint = new(
            _outbox.Count,
            _handled.Count,
            InboundCommitted: true,
            SnapshotCapturedCapabilityCauses(),
            _incoming.Checkpoint(),
            _outgoing.Checkpoint());
        _turnRollbacks.Clear();

        return turn;
    }

    internal async Task CompleteIncomingCapabilityRequestAsync(CapabilityTurn turn)
    {
        FlushOutgoing();
        await CommitAsync(CancellationToken.None);
        AdvanceTurnCheckpoint();
        await NotifyWatchersAsync();

        Restore(turn);
        ScheduleDrain();
    }

    internal void FailIncomingCapabilityRequest(CapabilityTurn turn)
    {
        Discard(
            _outbox,
            _turnCheckpoint?.CommittedOutbox ?? turn.CommittedOutbox);
        RestoreCapturedCapabilityCauses(
            _turnCheckpoint?.CapturedCapabilityCauses ?? turn.CapturedCapabilityCauses);
        _outgoing.Restore(_turnCheckpoint?.Outgoing ?? turn.Outgoing);
        RollbackTurnState();
        _firedWhileHandling.Clear();
        Restore(turn);
        ScheduleDrain();
    }

}
