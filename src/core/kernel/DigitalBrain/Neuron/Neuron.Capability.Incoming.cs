using DigitalBrain.Abstractions;

namespace DigitalBrain.Kernel;

public abstract partial class Neuron
{
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
        _outgoing.Restore(_turnCheckpoint?.Outgoing ?? turn.Outgoing);
        RollbackTurnState();
        _firedWhileHandling.Clear();
        Restore(turn);
        ScheduleDrain();
    }
}
