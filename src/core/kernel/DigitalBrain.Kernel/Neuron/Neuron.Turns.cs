using System.Collections.Concurrent;
using System.Reflection;
using DigitalBrain.Abstractions;
using Orleans.Journaling;

namespace DigitalBrain.Kernel;

public abstract partial class Neuron
{
    private static readonly ConcurrentDictionary<Type, bool> SettledFailureTypes = new();

    private bool HasAlreadyHandled(SynapseDelivery delivery)
        => _remembered.Contains(delivery.SynapseId);

    private static bool SettlesDelivery(Exception failure)
        => SettledFailureTypes.GetOrAdd(
            failure.GetType(),
            static type => type.GetCustomAttribute<SettledDeliveryFailureAttribute>() is not null);

    private void Remember(SynapseId delivered)
    {
        _handled.Add(delivered.Value);
        _remembered.Add(delivered);

        while (_handled.Count > RememberedDeliveryBound)
        {
            _remembered.Remove(new SynapseId(_handled[0]));
            _evictedWhileHandling.Add(_handled[0]);
            _handled.RemoveAt(0);
        }
    }

    // The handled mark is set membership, not a suffix. Once the window is full Remember evicts as
    // it adds, so the count a turn started at is reached again and truncating back to it retracts
    // nothing — the delivery would stay marked handled while its turn was thrown away, and the
    // outbox would swallow its own redelivery for good. A retraction has to name what this turn
    // added and put back what adding it pushed out.
    private void ForgetHandled(SynapseDelivery delivery)
    {
        for (var index = _handled.Count - 1; index >= 0; index--)
        {
            if (_handled[index] == delivery.SynapseId.Value)
            {
                _handled.RemoveAt(index);

                break;
            }
        }

        for (var index = _evictedWhileHandling.Count - 1; index >= 0; index--)
        {
            _handled.Insert(0, _evictedWhileHandling[index]);
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

    private Task DispatchAsync(Synapse synapse, CancellationToken cancellationToken)
        => SynapseDispatch.HandlersFor(GetType()).TryGetValue(synapse.GetType(), out var handler)
            ? handler(this, synapse, cancellationToken)
            : OnUnboundSynapseAsync(synapse, cancellationToken);

    // Runs inside the delivery turn, so anything emitted here inherits the delivery correlation.
    protected virtual Task OnUnboundSynapseAsync(Synapse synapse, CancellationToken cancellationToken)
        => Task.CompletedTask;

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

    // A capability request commits mid-turn, so what that commit produced — the outgoing record and
    // whatever the turn had already staged for the outbox — must survive a later retraction. The
    // inbound cause must not: it is the turn's outcome, not the request's.
    private void AdvanceTurnCheckpoint()
    {
        if (_turnCheckpoint is { } checkpoint)
        {
            _turnCheckpoint = checkpoint with
            {
                CommittedOutbox = _outbox.Count,
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

    private Synapse Snapshot(Synapse synapse)
        => _synapses.Deserialize(_synapses.SerializeToArray(synapse));

    internal readonly record struct CapabilityTurn(
        int CommittedOutbox,
        NeuronFeedCheckpoint Outgoing,
        IReadOnlyList<Action> PreviousRollbacks,
        SynapseDelivery? PreviousHandling,
        int PreviousDepth,
        TurnCheckpoint? PreviousCheckpoint);

    internal readonly record struct TurnCheckpoint(
        int CommittedOutbox,
        bool InboundCommitted,
        NeuronFeedCheckpoint Incoming,
        NeuronFeedCheckpoint Outgoing);
}
