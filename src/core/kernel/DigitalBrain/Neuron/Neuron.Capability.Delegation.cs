using DigitalBrain.Abstractions;

namespace DigitalBrain.Kernel;

public abstract partial class Neuron
{
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
        var delivery = SynapseDelivery.Create(fact, Id, sequence, delegation.Request, TimeProvider);
        var delegationCheckpoint = SnapshotDelegations();
        var outgoingCheckpoint = _outgoing.Checkpoint();

        try
        {
            FlushOutgoing();
            _outgoing.Append(delivery);
            _delegations[delegation.Identity] = _delegationStates.SerializeToArray(new(state.Delegation, terminal));
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
}
