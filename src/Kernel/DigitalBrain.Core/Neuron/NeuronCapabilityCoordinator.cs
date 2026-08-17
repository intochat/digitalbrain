using DigitalBrain.Abstractions;

namespace DigitalBrain.Core;

internal sealed class NeuronCapabilityCoordinator(
    Neuron neuron,
    NeuronJournal journal,
    NeuronOutbox outbox,
    NeuronTurnCoordinator turn)
{
    internal async Task<SynapseDelivery> BeginRequestAsync(
        string contract,
        string method,
        NeuronId target)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contract);
        ArgumentException.ThrowIfNullOrWhiteSpace(method);

        var sequence = journal.OutgoingNextSequence + turn.FiredCount;
        var delivery = SynapseDelivery.Create(
            new CapabilityRequested(contract, method, target),
            neuron.Id,
            sequence,
            turn.Handling,
            neuron.NeuronTimeProvider);

        turn.StageInboundCause();
        turn.FlushOutgoing();
        journal.AppendOutgoing(delivery);
        await outbox.CommitAsync(CancellationToken.None)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        turn.AdvanceCheckpoint();
        await journal.NotifyWatchersAsync()
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        return delivery;
    }

    internal async Task RecordOutcomeAsync(
        CapabilityOutcome outcome,
        SynapseDelivery request)
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

        var sequence = journal.OutgoingNextSequence + turn.FiredCount;
        var delivery = SynapseDelivery.Create(
            fact,
            neuron.Id,
            sequence,
            request,
            neuron.NeuronTimeProvider);

        turn.FlushOutgoing();
        journal.AppendOutgoing(delivery);
        await outbox.CommitAsync(CancellationToken.None)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        turn.AdvanceCheckpoint();
        await journal.NotifyWatchersAsync()
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    internal async Task RecordStreamedRequestAsync(
        SynapseDelivery delivery,
        GrainId? source)
    {
        ArgumentNullException.ThrowIfNull(delivery);

        RequireAuthorizedDelivery(delivery, source);

        var incomingCheckpoint = journal.IncomingCheckpoint();

        journal.AppendIncoming(delivery);
        turn.ProtectCommittedIncoming();

        try
        {
            await outbox.CommitAsync(CancellationToken.None)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }
        catch
        {
            journal.RestoreIncoming(incomingCheckpoint);
            turn.ProtectCommittedIncoming();
            throw;
        }

        await journal.NotifyWatchersAsync()
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    internal async Task<Neuron.CapabilityTurn> BeginIncomingRequestAsync(
        SynapseDelivery delivery,
        GrainId? source)
    {
        ArgumentNullException.ThrowIfNull(delivery);

        if (turn.Handling is not null)
        {
            throw new InvalidOperationException(
                $"Neuron '{neuron.Id}' cannot begin a capability request while it is already "
                + $"handling '{turn.Handling.SynapseId}'.");
        }

        RequireAuthorizedDelivery(delivery, source);

        var capabilityTurn = turn.CaptureCapabilityTurn();

        await CommitIncomingRequestAsync(delivery)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        await journal.NotifyWatchersAsync()
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        turn.EnterCapabilityTurn(delivery);

        return capabilityTurn;
    }

    internal async Task CompleteIncomingRequestAsync(Neuron.CapabilityTurn capabilityTurn)
    {
        turn.FlushOutgoing();
        await outbox.CommitAsync(CancellationToken.None)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        turn.AdvanceCheckpoint();
        await journal.NotifyWatchersAsync()
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        turn.RestoreCapabilityTurn(capabilityTurn);
        outbox.ScheduleDrain();
    }

    internal async Task FailIncomingRequestAsync(Neuron.CapabilityTurn capabilityTurn)
    {
        outbox.DiscardTo(turn.Checkpoint?.CommittedOutbox ?? capabilityTurn.CommittedOutbox);
        journal.RestoreOutgoing(turn.Checkpoint?.Outgoing ?? capabilityTurn.Outgoing);
        turn.RollbackTurnState();
        turn.ClearFired();

        await turn.CommitRetractionAsync()
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        turn.RestoreCapabilityTurn(capabilityTurn);
        outbox.ScheduleDrain();
    }

    private void RequireAuthorizedDelivery(SynapseDelivery delivery, GrainId? source)
    {
        if (delivery.Synapse is not CapabilityRequested request || request.Target != neuron.Id)
        {
            throw new InvalidOperationException(
                $"The capability request delivery does not target neuron '{neuron.Id}'.");
        }

        var sourceMatches = source is not null
            && NeuronId.FromGrainKey(
                source.Value.Type.ToString()
                    ?? throw new InvalidOperationException(
                        "The capability caller has no grain type."),
                source.Value.Key.ToString()) == delivery.Caller;

        if (!sourceMatches)
        {
            throw new NeuronAuthorizationException(
                $"The capability request caller '{delivery.Caller}' does not authorize its "
                + "actual Orleans source.");
        }
    }

    private async Task CommitIncomingRequestAsync(SynapseDelivery delivery)
    {
        var incomingCheckpoint = journal.IncomingCheckpoint();

        try
        {
            journal.AppendIncoming(delivery);
            await outbox.CommitAsync(CancellationToken.None)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }
        catch
        {
            journal.RestoreIncoming(incomingCheckpoint);
            throw;
        }
    }
}
