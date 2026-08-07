using DigitalBrain.Abstractions;

namespace DigitalBrain.Kernel;

public abstract partial class Neuron
{
    internal async Task RecordStreamedCapabilityRequestAsync(
        SynapseDelivery delivery,
        GrainId? source)
    {
        ArgumentNullException.ThrowIfNull(delivery);

        RequireAuthorizedCapabilityDelivery(delivery, source);

        var incomingCheckpoint = _incoming.Checkpoint();

        _incoming.Append(delivery);
        ProtectCommittedIncoming();

        try
        {
            await CommitAsync(CancellationToken.None).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }
        catch
        {
            _incoming.Restore(incomingCheckpoint);
            ProtectCommittedIncoming();

            throw;
        }

        await NotifyWatchersAsync().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    internal async Task<CapabilityTurn> BeginIncomingCapabilityRequestAsync(
        SynapseDelivery delivery,
        GrainId? source)
    {
        ArgumentNullException.ThrowIfNull(delivery);

        if (_handling is not null)
        {
            throw new InvalidOperationException(
                $"Neuron '{Id}' cannot begin a capability request while it is already handling '{_handling.SynapseId}'.");
        }

        RequireAuthorizedCapabilityDelivery(delivery, source);

        var turn = new CapabilityTurn(
            _outbox.Count,
            _outgoing.Checkpoint(),
            [.. _turnRollbacks],
            _handling,
            _handlingDepth,
            _turnCheckpoint);

        await CommitIncomingCapabilityRequestAsync(delivery).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        await NotifyWatchersAsync().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        _handling = delivery;
        _handlingDepth = DeliveryPolicy.InboundDepth();
        _turnCheckpoint = new(_outbox.Count, InboundCommitted: true, _incoming.Checkpoint(), _outgoing.Checkpoint());
        _turnRollbacks.Clear();

        return turn;
    }

    internal async Task CompleteIncomingCapabilityRequestAsync(CapabilityTurn turn)
    {
        FlushOutgoing();
        await CommitAsync(CancellationToken.None).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        AdvanceTurnCheckpoint();
        await NotifyWatchersAsync().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        Restore(turn);
        ScheduleDrain();
    }

    private void RequireAuthorizedCapabilityDelivery(
        SynapseDelivery delivery,
        GrainId? source)
    {
        if (delivery.Synapse is not CapabilityRequested request || request.Target != Id)
        {
            throw new InvalidOperationException(
                $"The capability request delivery does not target neuron '{Id}'.");
        }

        var sourceMatches = source is not null
            && NeuronId.FromGrainKey(
                source.Value.Type.ToString()
                    ?? throw new InvalidOperationException("The capability caller has no grain type."),
                source.Value.Key.ToString()) == delivery.Caller;

        if (!sourceMatches)
        {
            throw new NeuronAuthorizationException(
                $"The capability request caller '{delivery.Caller}' does not authorize its actual Orleans source.");
        }
    }

    private async Task CommitIncomingCapabilityRequestAsync(SynapseDelivery delivery)
    {
        var incomingCheckpoint = _incoming.Checkpoint();

        try
        {
            _incoming.Append(delivery);
            await CommitAsync(CancellationToken.None).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }
        catch
        {
            _incoming.Restore(incomingCheckpoint);

            throw;
        }
    }

    internal async Task FailIncomingCapabilityRequestAsync(CapabilityTurn turn)
    {
        Discard(_outbox, _turnCheckpoint?.CommittedOutbox ?? turn.CommittedOutbox);
        _outgoing.Restore(_turnCheckpoint?.Outgoing ?? turn.Outgoing);
        RollbackTurnState();
        _firedWhileHandling.Clear();

        await CommitRetractionAsync().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        Restore(turn);
        ScheduleDrain();
    }
}
