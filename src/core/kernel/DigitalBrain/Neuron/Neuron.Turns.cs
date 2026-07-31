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

    private Task DispatchAsync(Synapse synapse, CancellationToken cancellationToken)
        => SynapseDispatch.HandlersFor(GetType()).TryGetValue(synapse.GetType(), out var handler)
            ? handler(this, synapse, cancellationToken)
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
        int CommittedHandled,
        bool InboundCommitted,
        NeuronFeedCheckpoint Incoming,
        NeuronFeedCheckpoint Outgoing);
}
