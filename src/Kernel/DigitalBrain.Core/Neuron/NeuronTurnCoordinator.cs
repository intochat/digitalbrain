using System.Diagnostics;
using DigitalBrain.Abstractions;
using Orleans.Serialization;

namespace DigitalBrain.Core;

internal sealed class NeuronTurnCoordinator(
    Neuron neuron,
    NeuronJournal journal,
    NeuronOutbox outbox,
    NeuronDeliveryMemory deliveries,
    Serializer<Synapse> synapses)
{
    private readonly List<SynapseDelivery> _firedWhileHandling = [];
    private readonly List<Action> _turnRollbacks = [];
    private SynapseDelivery? _handling;
    private Neuron.TurnCheckpoint? _turnCheckpoint;

    internal SynapseDelivery? Handling => _handling;

    internal Neuron.TurnCheckpoint? Checkpoint => _turnCheckpoint;

    internal int CurrentDepth { get; set; }

    internal CancellationToken CancellationToken { get; private set; }

    internal int FiredCount => _firedWhileHandling.Count;

    internal long NextOutgoingSequence
        => journal.OutgoingNextSequence + (_handling is null ? 0 : _firedWhileHandling.Count);

    internal void Activate() => deliveries.Activate();

    internal async Task DeliverAsync(
        SynapseDelivery delivery,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        cancellationToken.ThrowIfCancellationRequested();

        if (deliveries.Contains(delivery))
        {
            return;
        }

        using var handling = SynapseTelemetry.Source.StartActivity("handle");

        handling?.SetTag(SynapseTelemetry.ReceiverTag, neuron.Id.ToString());
        handling?.SetTag(SynapseTelemetry.SynapseTag, delivery.Synapse.GetType().Name);
        handling?.SetTag(SynapseTelemetry.CorrelationTag, delivery.CorrelationId.ToString());

        _handling = delivery;
        CurrentDepth = DeliveryPolicy.InboundDepth();
        CancellationToken = cancellationToken;

        var previousCheckpoint = _turnCheckpoint;
        _turnCheckpoint = new Neuron.TurnCheckpoint(
            outbox.Count,
            InboundCommitted: false,
            journal.IncomingCheckpoint(),
            journal.OutgoingCheckpoint());

        _firedWhileHandling.Clear();
        deliveries.BeginTurn();
        _turnRollbacks.Clear();

        try
        {
            await neuron.DispatchSynapseAsync(Snapshot(delivery.Synapse), cancellationToken)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

            FlushOutgoing();
            StageInboundCause();

            await outbox.CommitAsync(cancellationToken)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            AdvanceCheckpoint();

            await journal.NotifyWatchersAsync()
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }
        catch (Exception failure)
        {
            handling?.SetStatus(ActivityStatusCode.Error, failure.Message);

            var checkpoint = _turnCheckpoint
                ?? throw new InvalidOperationException(
                    "The handling turn lost its durable checkpoint.");

            outbox.DiscardTo(checkpoint.CommittedOutbox);
            journal.RestoreOutgoing(checkpoint.Outgoing);

            if (NeuronDeliveryMemory.Settles(failure))
            {
                StageInboundCause();
            }
            else
            {
                deliveries.Forget(delivery);
                journal.RestoreIncoming(checkpoint.Incoming);
            }

            RollbackTurnState();

            await CommitRetractionAsync()
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

            deliveries.Activate();
            outbox.ScheduleDrain();

            throw;
        }
        finally
        {
            _firedWhileHandling.Clear();
            deliveries.EndTurn();
            _turnRollbacks.Clear();
            _handling = null;
            CurrentDepth = 0;
            CancellationToken = default;
            _turnCheckpoint = previousCheckpoint;
        }

        outbox.ScheduleDrain();
    }

    internal Synapse Snapshot(Synapse synapse)
        => synapses.Deserialize(synapses.SerializeToArray(synapse));

    internal void StageOutgoing(SynapseDelivery delivery)
    {
        if (_handling is null)
        {
            journal.AppendOutgoing(delivery);
        }
        else
        {
            _firedWhileHandling.Add(delivery);
        }
    }

    internal void FlushOutgoing()
    {
        foreach (var fired in _firedWhileHandling)
        {
            journal.AppendOutgoing(fired);
        }

        _firedWhileHandling.Clear();
    }

    internal void StageInboundCause()
    {
        if (_handling is null
            || _turnCheckpoint is not { InboundCommitted: false } checkpoint)
        {
            return;
        }

        journal.AppendIncoming(_handling);
        deliveries.Remember(_handling.SynapseId);
        _turnCheckpoint = checkpoint with { InboundCommitted = true };
    }

    internal void AdvanceCheckpoint()
    {
        if (_turnCheckpoint is { } checkpoint)
        {
            _turnCheckpoint = checkpoint with
            {
                CommittedOutbox = outbox.Count,
                Outgoing = journal.OutgoingCheckpoint(),
            };
            _turnRollbacks.Clear();
        }
    }

    internal void ProtectCommittedIncoming()
    {
        if (_turnCheckpoint is { } checkpoint)
        {
            _turnCheckpoint = checkpoint with { Incoming = journal.IncomingCheckpoint() };
        }
    }

    internal void EnlistRollback(Action rollback)
    {
        ArgumentNullException.ThrowIfNull(rollback);

        if (_handling is null || _turnCheckpoint is null)
        {
            throw new InvalidOperationException(
                $"Neuron '{neuron.Id}' can enlist rollback only while handling a durable turn.");
        }

        _turnRollbacks.Add(rollback);
    }

    internal void ValidateCapabilityCaller(NeuronId expectedCaller)
        => _ = CurrentCapabilityRequestFrom(expectedCaller);

    internal Neuron.CapabilityTurn CaptureCapabilityTurn()
        => new(
            outbox.Count,
            journal.OutgoingCheckpoint(),
            [.. _turnRollbacks],
            _handling,
            CurrentDepth,
            _turnCheckpoint);

    internal void EnterCapabilityTurn(SynapseDelivery delivery)
    {
        _handling = delivery;
        CurrentDepth = DeliveryPolicy.InboundDepth();
        _turnCheckpoint = new Neuron.TurnCheckpoint(
            outbox.Count,
            InboundCommitted: true,
            journal.IncomingCheckpoint(),
            journal.OutgoingCheckpoint());
        _turnRollbacks.Clear();
    }

    internal void RestoreCapabilityTurn(Neuron.CapabilityTurn turn)
    {
        _turnRollbacks.Clear();
        _turnRollbacks.AddRange(turn.PreviousRollbacks);
        _handling = turn.PreviousHandling;
        CurrentDepth = turn.PreviousDepth;
        _turnCheckpoint = turn.PreviousCheckpoint;
    }

    internal void RollbackTurnState()
    {
        for (var index = _turnRollbacks.Count - 1; index >= 0; index--)
        {
            _turnRollbacks[index]();
        }

        _turnRollbacks.Clear();
    }

    internal void ClearFired() => _firedWhileHandling.Clear();

    internal async Task CommitRetractionAsync()
    {
        try
        {
            await outbox.CommitAsync(CancellationToken.None)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }
        catch (Exception unretracted)
        {
            SynapseTelemetry.RetractionUncommitted(neuron.Id, unretracted);
        }
    }

    private SynapseDelivery CurrentCapabilityRequestFrom(NeuronId expectedCaller)
    {
        if (expectedCaller == default)
        {
            throw new ArgumentException(
                "A capability causation caller is required.",
                nameof(expectedCaller));
        }

        var delivery = _handling
            ?? throw new InvalidOperationException(
                $"Neuron '{neuron.Id}' can validate a capability caller only while handling "
                + "a committed capability request.");

        if (_turnCheckpoint is not { InboundCommitted: true })
        {
            throw new InvalidOperationException(
                $"Neuron '{neuron.Id}' can validate a capability caller only after its incoming "
                + "capability request has been committed.");
        }

        if (delivery.Synapse is not CapabilityRequested request || request.Target != neuron.Id)
        {
            throw new InvalidOperationException(
                $"Neuron '{neuron.Id}' can validate only a committed capability request "
                + "targeting itself.");
        }

        if (delivery.Caller != expectedCaller)
        {
            throw new NeuronAuthorizationException(
                $"Capability request '{delivery.SynapseId}' was sent by '{delivery.Caller}', "
                + $"not expected caller '{expectedCaller}'.");
        }

        return delivery;
    }
}
