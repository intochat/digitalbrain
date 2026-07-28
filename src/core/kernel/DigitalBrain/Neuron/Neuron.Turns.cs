using DigitalBrain.Abstractions;
using Orleans.Journaling;

namespace DigitalBrain.Kernel;

public abstract partial class Neuron
{
    private bool HasAlreadyHandled(SynapseDelivery delivery)
        => _remembered.Contains(delivery.SynapseId);

    private void Remember(SynapseId delivered)
    {
        _handled.Add(delivered.Value);
        _remembered.Add(delivered);

        while (_handled.Count > RememberedDeliveries)
        {
            _remembered.Remove(new SynapseId(_handled[0]));
            _handled.RemoveAt(0);
        }
    }

    private void RecallHandledDeliveries()
    {
        _remembered.Clear();

        foreach (var delivered in _handled)
        {
            _remembered.Add(new SynapseId(delivered));
        }
    }

    private Task DispatchAsync(Synapse synapse)
        => SynapseDispatch.HandlersFor(GetType()).TryGetValue(synapse.GetType(), out var handler)
            ? handler(this, synapse, CancellationToken.None)
            : Task.CompletedTask;

    private void FlushOutgoing()
    {
        foreach (var fired in _firedWhileHandling)
        {
            _outgoing.Append(fired);
        }

        _firedWhileHandling.Clear();
    }

    private void Restore(CapabilityTurn turn)
    {
        _turnRollbacks.Clear();
        _turnRollbacks.AddRange(turn.PreviousRollbacks);
        _handling = turn.PreviousHandling;
        _handlingDepth = turn.PreviousDepth;
        _turnCheckpoint = turn.PreviousCheckpoint;
    }

    private void AdvanceTurnCheckpoint()
    {
        if (_turnCheckpoint is { } checkpoint)
        {
            _turnCheckpoint = checkpoint with
            {
                CommittedOutbox = _outbox.Count,
                CommittedHandled = _handled.Count,
                Incoming = _incoming.Checkpoint(),
                Outgoing = _outgoing.Checkpoint(),
            };
            _turnRollbacks.Clear();
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "A retraction commit failure must not replace the turn failure that caused it.")]
    private async Task CommitRetractionAsync()
    {
        try
        {
            await CommitAsync(CancellationToken.None);
        }
        catch (Exception unretracted)
        {
            SynapseTelemetry.RetractionUncommitted(Id, unretracted);
        }
    }

    private void ProtectCommittedIncoming()
    {
        if (_turnCheckpoint is { } checkpoint)
        {
            _turnCheckpoint = checkpoint with { Incoming = _incoming.Checkpoint() };
        }
    }

    private void RollbackTurnState()
    {
        for (var index = _turnRollbacks.Count - 1; index >= 0; index--)
        {
            _turnRollbacks[index]();
        }

        _turnRollbacks.Clear();
    }

    private void StageInboundCause()
    {
        if (_handling is null
            || _turnCheckpoint is not { InboundCommitted: false } checkpoint)
        {
            return;
        }

        _incoming.Append(_handling);
        Remember(_handling.SynapseId);
        _turnCheckpoint = checkpoint with { InboundCommitted = true };
    }

    private DelegationCheckpoint SnapshotDelegations()
        => new(
            _delegations.ToDictionary(
                entry => entry.Key,
                entry => entry.Value.ToArray()),
            [.. _delegationConsumed],
            [.. _delegationTerminals]);

    private void RestoreDelegations(DelegationCheckpoint checkpoint)
    {
        foreach (var key in _delegations.Select(entry => entry.Key).ToArray())
        {
            _delegations.Remove(key);
        }

        foreach (var entry in checkpoint.States)
        {
            _delegations[entry.Key] = entry.Value.ToArray();
        }

        Replace(_delegationConsumed, checkpoint.Consumed);
        Replace(_delegationTerminals, checkpoint.Terminals);
    }

    private static void Replace(IDurableList<Guid> target, IReadOnlyList<Guid> values)
    {
        Discard(target, 0);

        foreach (var value in values)
        {
            target.Add(value);
        }
    }

    private Synapse Snapshot(Synapse synapse)
        => _synapses.Deserialize(_synapses.SerializeToArray(synapse));

    private void MakeRoomForDelegation()
    {
        while (_delegations.Count >= MaximumRememberedDelegations)
        {
            if (TryEvictOldest(_delegationTerminals, ProtectedTerminalDelegations))
            {
                continue;
            }

            if (TryEvictOldest(_delegationConsumed, ProtectedConsumedDelegations))
            {
                continue;
            }

            break;
        }

        if (_delegations.Count >= MaximumRememberedDelegations)
        {
            throw new InvalidOperationException(
                $"Neuron '{Id}' has reached its limit of {MaximumRememberedDelegations} remembered capability delegations, with no safely evictable terminal or consumed history. Resolve an issued delegation or finish another consumed delegation before minting another.");
        }
    }

    private bool TryEvictOldest(IDurableList<Guid> retentionOrder, int protectedDelegations)
    {
        if (retentionOrder.Count <= protectedDelegations)
        {
            return false;
        }

        var evicted = retentionOrder[0];
        retentionOrder.RemoveAt(0);

        if (!_delegations.Remove(evicted))
        {
            throw new InvalidOperationException(
                "The durable capability delegation retention order references missing state.");
        }

        return true;
    }

    private static int IndexOf(IDurableList<Guid> retentionOrder, Guid identity)
    {
        for (var index = 0; index < retentionOrder.Count; index++)
        {
            if (retentionOrder[index] == identity)
            {
                return index;
            }
        }

        return -1;
    }

    internal readonly record struct CapabilityTurn(
        int CommittedOutbox,
        NeuronFeedCheckpoint Outgoing,
        IReadOnlyList<Action> PreviousRollbacks,
        SynapseDelivery? PreviousHandling,
        int PreviousDepth,
        TurnCheckpoint? PreviousCheckpoint);

    private readonly record struct DelegationCheckpoint(
        IReadOnlyDictionary<Guid, byte[]> States,
        IReadOnlyList<Guid> Consumed,
        IReadOnlyList<Guid> Terminals);

    internal readonly record struct TurnCheckpoint(
        int CommittedOutbox,
        int CommittedHandled,
        bool InboundCommitted,
        NeuronFeedCheckpoint Incoming,
        NeuronFeedCheckpoint Outgoing);
}
