using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace DigitalBrain;

internal sealed class Outbox(
    NeuronHost owner,
    Journal journal,
    Router router,
    ISynapseSerialization serialization,
    IEnvelopeCarrier envelopes,
    DigitalBrainClock clock)
{
    private IGrainTimer? retryTimer;
    private bool wakeupArmed;

    internal Task PrepareRecordAsync()
        => journal.HasUnrecordedPending() ? ArmWakeupAsync() : Task.CompletedTask;

    internal void Kick()
    {
        if (journal.HasPending())
        {
            EnsureRetryTimer();
        }
    }

    internal async Task DrainAsync(CancellationToken cancellationToken)
    {
        try
        {
            var entry = journal.NextPending();
            if (entry is null)
            {
                await StopAsync();
                return;
            }

            var pending = journal.ProgressOf(entry.Position)?.Pending ?? entry.DeliveryTargets;
            var attempts = (journal.ProgressOf(entry.Position)?.Attempts ?? 0) + 1;
            var remaining = new List<DeliveryTarget>();
            var terminal = new List<(DeliveryTarget Receiver, string Reason)>();

            foreach (var receiver in pending)
            {
                var result = await TryDeliverAsync(entry, receiver, cancellationToken);
                if (result.Rejected)
                {
                    continue;
                }

                if (result.Reason is { } reason)
                {
                    terminal.Add((receiver, reason));
                }
                else if (!result.Delivered)
                {
                    remaining.Add(receiver);
                }
            }

            if (attempts >= DeliveryPolicy.MaximumAttempts
                || clock.UtcNow - entry.Origin.OccurredAt >= DeliveryPolicy.RetryHorizon)
            {
                terminal.AddRange(remaining.Select(receiver => (receiver, "delivery retry horizon exhausted")));
                remaining.Clear();
            }

            if (remaining.Count == 0)
            {
                journal.Settle(entry.Position);
            }
            else
            {
                journal.SetProgress(entry.Position, new DeliveryProgress([.. remaining], attempts));
            }

            var causedBy = new SynapseReference(owner.Id, entry.Position);
            // DeliveryFailed is already the terminal outcome. A receiver that cannot
            // accept it must not create another durable failure-reporting chain.
            if (DeliveryFailurePolicy.ShouldProduceFor(entry.SynapseKind))
            {
                foreach (var (receiver, reason) in terminal)
                {
                    owner.ProduceDeliveryFailure(
                        new SynapseReference(owner.Id, entry.Position),
                        receiver.ToNeuronId(),
                        reason,
                        attempts,
                        causedBy);
                }
            }

            await PrepareRecordAsync();
            await owner.RecordAsync();
        }
        catch
        {
            owner.Poison();
            throw;
        }

        if (!journal.HasPending())
        {
            await StopAsync();
        }
    }

    private async Task ArmWakeupAsync()
    {
        if (!wakeupArmed)
        {
            await owner.RuntimeGrainFactory
                .GetGrain<IOutboxWakeup>(OutboxWakeup.AddressOf(owner.Address))
                .ArmAsync();
            wakeupArmed = true;
        }
    }

    private async Task StopAsync()
    {
        retryTimer?.Dispose();
        retryTimer = null;
        await owner.RuntimeGrainFactory
            .GetGrain<IOutboxWakeup>(OutboxWakeup.AddressOf(owner.Address))
            .DisarmAsync();
        wakeupArmed = false;
    }

    private void EnsureRetryTimer()
    {
        retryTimer ??= owner.RegisterOutboxTimer(
            static (outbox, cancellationToken) => outbox.DrainAsync(cancellationToken),
            this);
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "A remote receiver failure is transient until the durable retry bound is reached.")]
    private async Task<DeliveryResult> TryDeliverAsync(
        StoredJournalRecord entry,
        DeliveryTarget target,
        CancellationToken cancellationToken)
    {
        var receiver = target.ToNeuronId();
        if (!router.IsKnown(receiver))
        {
            return DeliveryResult.Terminal($"neuron kind '{receiver.Kind}' is absent from the catalog");
        }

        Synapse? synapse;
        try
        {
            synapse = serialization.DeserializeForDispatch(entry.SynapseKind, entry.Serialization);
        }
        catch (JsonException failure)
        {
            return DeliveryResult.Terminal(failure.Message);
        }

        if (synapse is null)
        {
            return DeliveryResult.Terminal($"synapse kind '{entry.SynapseKind}' is absent from the catalog");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(DeliveryPolicy.AttemptTimeout);
        try
        {
            envelopes.Write(entry.ToEnvelope());
            var host = owner.RuntimeGrainFactory.GetGrain<INeuronHost>(
                NeuronHost.AddressOf(new ScopedNeuronAddress(owner.Scope, receiver)));
            return await NeuronHost.WireDelivererFor(synapse.GetType())(host, synapse, timeout.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            return DeliveryResult.Transient;
        }
        catch (Exception)
        {
            return DeliveryResult.Transient;
        }
    }
}
