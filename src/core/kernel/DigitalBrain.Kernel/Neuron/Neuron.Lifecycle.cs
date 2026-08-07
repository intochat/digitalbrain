using System.Diagnostics;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Kernel;

public abstract partial class Neuron
{
    public sealed override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        NeuronConcurrency.RequireSerializedTurns(GetType());

        await base.OnActivateAsync(cancellationToken).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        RecallHandledDeliveries();

        if (_outbox.Count > 0)
        {
            await Wakeup().Arm().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            _wakeUpRegistered = true;
        }
        else
        {
            _wakeUpRegistered = false;
        }

        ScheduleDrain();

        await OnNeuronActivatedAsync(cancellationToken).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    // Runs on every activation once journals, outbox and drain are restored, so a neuron can
    // repair state it publishes outside itself and cannot otherwise notice has diverged.
    protected virtual Task OnNeuronActivatedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

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
        _turnCheckpoint = new(_outbox.Count, InboundCommitted: false, _incoming.Checkpoint(), _outgoing.Checkpoint());

        _firedWhileHandling.Clear();
        _evictedWhileHandling.Clear();
        _turnRollbacks.Clear();

        try
        {
            await DispatchAsync(Snapshot(delivery.Synapse), cancellationToken).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

            FlushOutgoing();
            StageInboundCause();

            await CommitAsync(cancellationToken).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            AdvanceTurnCheckpoint();

            await NotifyWatchersAsync().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }
        catch (Exception failure)
        {
            handling?.SetStatus(ActivityStatusCode.Error, failure.Message);

            var checkpoint = _turnCheckpoint
                ?? throw new InvalidOperationException("The handling turn lost its durable checkpoint.");

            Discard(_outbox, checkpoint.CommittedOutbox);
            _outgoing.Restore(checkpoint.Outgoing);

            if (SettlesDelivery(failure))
            {
                // The failure is this delivery's answer, so the fact stays received and handled
                // whether or not the turn got as far as journaling its cause.
                StageInboundCause();
            }
            else
            {
                ForgetHandled(delivery);
                _incoming.Restore(checkpoint.Incoming);
            }

            RollbackTurnState();

            await CommitRetractionAsync().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

            RecallHandledDeliveries();
            ScheduleDrain();

            throw;
        }
        finally
        {
            _firedWhileHandling.Clear();
            _evictedWhileHandling.Clear();
            _turnRollbacks.Clear();
            _handling = null;
            _handlingDepth = 0;
            _turnCancellation = default;
            _turnCheckpoint = previousCheckpoint;
        }

        ScheduleDrain();
    }

}
