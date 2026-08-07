using System.Diagnostics;
using DigitalBrain.Abstractions;
using Orleans.Journaling;

namespace DigitalBrain.Core;

public abstract partial class Neuron
{
    private async Task CommitAsync(CancellationToken cancellationToken)
    {
        if (_outbox.Count > 0 && !_wakeUpRegistered)
        {
            await Wakeup().Arm().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            _wakeUpRegistered = true;
        }

        await WriteStateAsync(cancellationToken).ConfigureAwait(true);

        await ForgetWakeUpWhenOutboxIsEmptyAsync().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    private async Task ForgetWakeUpWhenOutboxIsEmptyAsync()
    {
        if (_outbox.Count > 0 || !_wakeUpRegistered)
        {
            return;
        }

        await Wakeup().Disarm().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        _wakeUpRegistered = false;
    }

    async Task IOutboxDrain.Drain()
    {
        _wakeUpRegistered = true;
        // Reminder/wakeup path has no caller token. Use a cancelable lifecycle source so every
        // Deliver attempt can link a real abort signal; attempt timeout supplies the bound.
        using var drainLifecycle = new CancellationTokenSource();
        await DrainAsync(drainLifecycle.Token).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    private IOutboxWakeup Wakeup()
        => GrainFactory.GetGrain<IOutboxWakeup>(Id.ToString());

    private static void Discard<TEntry>(IDurableList<TEntry> journal, int committed)
    {
        while (journal.Count > committed)
        {
            journal.RemoveAt(journal.Count - 1);
        }
    }

    private void ScheduleDrain()
    {
        if (_outbox.Count == 0 || _draining is not null)
        {
            return;
        }

        _draining = this.RegisterGrainTimer(DrainAsync, RetryInterval, RetryInterval);
    }

    private void StopDrainingWhenOutboxIsEmpty()
    {
        if (_outbox.Count > 0 || _draining is null)
        {
            return;
        }

        _draining.Dispose();
        _draining = null;
    }

    private async Task DrainAsync(CancellationToken cancellationToken)
    {
        var blockedTargets = new HashSet<NeuronId>();

        for (var index = 0; index < _outbox.Count;)
        {
            var committed = _entries.Deserialize(_outbox[index]);

            if (committed.Depth > DeliveryPolicy.MaximumDepth)
            {
                Abandon(committed, $"exceeded the maximum synapse depth of {DeliveryPolicy.MaximumDepth}");
                _outbox.RemoveAt(index);

                continue;
            }

            var attemptable = committed.Pending.Where(receiver => !blockedTargets.Contains(receiver)).ToArray();

            if (attemptable.Length == 0)
            {
                foreach (var receiver in committed.Pending)
                {
                    blockedTargets.Add(receiver);
                }

                index++;

                continue;
            }

            var entry = committed with { Attempts = committed.Attempts + 1 };
            var stillPending = new List<NeuronId>();

            foreach (var receiver in committed.Pending)
            {
                if (blockedTargets.Contains(receiver))
                {
                    stillPending.Add(receiver);

                    continue;
                }

                if (await TryDeliverAsync(entry, receiver, cancellationToken).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext))
                {
                    continue;
                }

                stillPending.Add(receiver);
                blockedTargets.Add(receiver);
            }

            if (stillPending.Count == 0)
            {
                _outbox.RemoveAt(index);

                continue;
            }

            if (Exhausted(entry))
            {
                Abandon(entry with { Pending = [.. stillPending] },
                    $"undeliverable to {string.Join(", ", stillPending)} after {entry.Attempts} attempts");
                _outbox.RemoveAt(index);

                foreach (var receiver in stillPending)
                {
                    blockedTargets.Remove(receiver);
                }

                continue;
            }

            _outbox[index] = _entries.SerializeToArray(entry with { Pending = [.. stillPending] });
            index++;
        }

        await CommitAsync(cancellationToken).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        StopDrainingWhenOutboxIsEmpty();
    }

    private bool Exhausted(OutboxEntry entry)
        => entry.Attempts >= DeliveryPolicy.MaximumAttempts
        || TimeProvider.GetUtcNow() - entry.Delivery.Timestamp
            > DeliveryPolicy.RetryHorizon;

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Any failure other than a permanent refusal keeps the receiver pending so the outbox redelivers it; letting it escape would abandon the delivery guarantee.")]
    private async Task<bool> TryDeliverAsync(
        OutboxEntry entry,
        NeuronId receiver,
        CancellationToken drainToken)
    {
        DeliveryPolicy.CarryDepth(entry.Depth);

        // Every Deliver attempt gets a cancelable, bounded token. Link any real upstream drain
        // token; always attach a finite attempt timeout so the token can actually cancel.
        using var attemptCts = drainToken.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(drainToken)
            : new CancellationTokenSource();
        attemptCts.CancelAfter(DeliveryPolicy.DeliveryAttemptTimeout);
        var attemptToken = attemptCts.Token;

        try
        {
            if (receiver == Id)
            {
                await Deliver(entry.Delivery, attemptToken).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            }
            else
            {
                await GrainFactory.GetGrain<INeuron>(receiver.ToGrainId()).Deliver(entry.Delivery, attemptToken).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            }

            return true;
        }
        catch (NeuronAuthorizationException refusal)
        {
            Record("refused", entry.Delivery, receiver, refusal.Message);

            return true;
        }
        catch (OperationCanceledException) when (attemptToken.IsCancellationRequested)
        {
            // Cancelled/expired attempt: leave pending for retry; never remove or bypass retraction.
            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static void Abandon(OutboxEntry entry, string reason)
    {
        foreach (var receiver in entry.Pending)
        {
            Record("abandoned", entry.Delivery, receiver, reason);
        }
    }

    private static void Record(string outcome, SynapseDelivery delivery, NeuronId receiver, string reason)
    {
        using var recorded = SynapseTelemetry.Source.StartActivity(outcome);

        recorded?.SetTag(SynapseTelemetry.ReceiverTag, receiver.ToString());
        recorded?.SetTag(SynapseTelemetry.SynapseTag, delivery.Synapse.GetType().Name);
        recorded?.SetTag(SynapseTelemetry.CorrelationTag, delivery.CorrelationId.ToString());
        recorded?.SetStatus(ActivityStatusCode.Error, reason);
    }

}
