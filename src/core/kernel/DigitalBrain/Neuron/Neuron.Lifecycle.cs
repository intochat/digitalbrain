using System.Diagnostics;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Kernel;

public abstract partial class Neuron
{
    public sealed override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        NeuronConcurrency.RequireSerializedTurns(GetType());

        await base.OnActivateAsync(cancellationToken);

        RecallHandledDeliveries();

        if (_outbox.Count > 0)
        {
            await Wakeup().Arm();
            _wakeUpRegistered = true;
        }
        else
        {
            _wakeUpRegistered = false;
        }

        ScheduleDrain();
    }

    public async Task Deliver(SynapseDelivery delivery, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        cancellationToken.ThrowIfCancellationRequested();

        if (HasAlreadyHandled(delivery))
        {
            return;
        }

        using var handling = SynapseTelemetry.Source.StartActivity("handle");

        handling?.SetTag(SynapseTelemetry.ReceiverTag, Id.ToString());
        handling?.SetTag(SynapseTelemetry.SynapseTag, delivery.Synapse.GetType().Name);
        handling?.SetTag(SynapseTelemetry.CorrelationTag, delivery.CorrelationId.ToString());

        _handling = delivery;
        _handlingDepth = DeliveryPolicy.InboundDepth();
        _turnCancellation = cancellationToken;

        var previousCheckpoint = _turnCheckpoint;
        _turnCheckpoint = new(_outbox.Count, _handled.Count, InboundCommitted: false, _incoming.Checkpoint(), _outgoing.Checkpoint());

        _firedWhileHandling.Clear();
        _turnRollbacks.Clear();

        try
        {
            await DispatchAsync(Snapshot(delivery.Synapse), cancellationToken);

            FlushOutgoing();
            StageInboundCause();

            await CommitAsync(cancellationToken);
            AdvanceTurnCheckpoint();

            await NotifyWatchersAsync();
        }
        catch (Exception failure)
        {
            handling?.SetStatus(ActivityStatusCode.Error, failure.Message);

            var checkpoint = _turnCheckpoint
                ?? throw new InvalidOperationException("The handling turn lost its durable checkpoint.");

            Discard(_outbox, checkpoint.CommittedOutbox);
            Discard(_handled, checkpoint.CommittedHandled);
            _incoming.Restore(checkpoint.Incoming);
            _outgoing.Restore(checkpoint.Outgoing);
            RollbackTurnState();

            await CommitRetractionAsync();

            RecallHandledDeliveries();
            ScheduleDrain();

            throw;
        }
        finally
        {
            _firedWhileHandling.Clear();
            _turnRollbacks.Clear();
            _handling = null;
            _handlingDepth = 0;
            _turnCancellation = default;
            _turnCheckpoint = previousCheckpoint;
        }

        ScheduleDrain();
    }

}
