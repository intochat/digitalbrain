using System.Diagnostics;
using DigitalBrain.Abstractions;
using Orleans.Journaling;
using Orleans.Serialization;

namespace DigitalBrain.Core;

internal sealed class NeuronOutbox(
    Neuron neuron,
    IDurableList<byte[]> entries,
    Serializer<OutboxEntry> serializer)
{
    private static readonly TimeSpan RetryInterval = TimeSpan.FromMilliseconds(50);

    // Outcomes are staged here and journaled only after the drain loop finishes: the loop
    // indexes and mutates `entries`, so appending mid-iteration would corrupt the cursor.
    private readonly List<(SynapseDelivery Cause, Synapse Outcome)> _outcomes = [];

    private IGrainTimer? _draining;
    private bool _wakeUpRegistered;

    internal int Count => entries.Count;

    internal void Add(OutboxEntry entry)
        => entries.Add(serializer.SerializeToArray(entry));

    internal void DiscardTo(int committed)
    {
        while (entries.Count > committed)
        {
            entries.RemoveAt(entries.Count - 1);
        }
    }

    internal async Task ActivateAsync()
    {
        if (entries.Count > 0)
        {
            await Wakeup().Arm()
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            _wakeUpRegistered = true;
        }
        else
        {
            _wakeUpRegistered = false;
        }

        ScheduleDrain();
    }

    internal async Task CommitAsync(CancellationToken cancellationToken)
    {
        if (entries.Count > 0 && !_wakeUpRegistered)
        {
            await Wakeup().Arm()
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            _wakeUpRegistered = true;
        }

        await neuron.WriteNeuronStateAsync(cancellationToken).ConfigureAwait(true);

        await ForgetWakeUpWhenEmptyAsync()
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    internal async Task DrainFromWakeupAsync()
    {
        _wakeUpRegistered = true;
        using var drainLifecycle = new CancellationTokenSource();
        await DrainAsync(drainLifecycle.Token)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    internal Task FlushAsync(CancellationToken cancellationToken)
        => DrainAsync(cancellationToken);

    internal void ScheduleDrain()
    {
        if (entries.Count == 0 || _draining is not null)
        {
            return;
        }

        _draining = neuron.RegisterNeuronTimer(DrainAsync, RetryInterval, RetryInterval);
    }

    private IOutboxWakeup Wakeup()
        => neuron.NeuronGrainFactory.GetGrain<IOutboxWakeup>(neuron.Id.ToString());

    private async Task ForgetWakeUpWhenEmptyAsync()
    {
        if (entries.Count > 0 || !_wakeUpRegistered)
        {
            return;
        }

        await Wakeup().Disarm()
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        _wakeUpRegistered = false;
    }

    private void StopDrainingWhenEmpty()
    {
        if (entries.Count > 0 || _draining is null)
        {
            return;
        }

        _draining.Dispose();
        _draining = null;
    }

    private async Task DrainAsync(CancellationToken cancellationToken)
    {
        var blockedTargets = new HashSet<NeuronId>();
        _outcomes.Clear();

        for (var index = 0; index < entries.Count;)
        {
            var committed = serializer.Deserialize(entries[index]);

            if (committed.Depth > DeliveryPolicy.MaximumDepth)
            {
                Abandon(
                    committed,
                    $"exceeded the maximum synapse depth of {DeliveryPolicy.MaximumDepth}");
                entries.RemoveAt(index);
                continue;
            }

            var attemptable = committed.Pending
                .Where(receiver => !blockedTargets.Contains(receiver))
                .ToArray();

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

                if (await TryDeliverAsync(entry, receiver, cancellationToken)
                    .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext))
                {
                    continue;
                }

                stillPending.Add(receiver);
                blockedTargets.Add(receiver);
            }

            if (stillPending.Count == 0)
            {
                entries.RemoveAt(index);
                continue;
            }

            if (Exhausted(entry))
            {
                Abandon(
                    entry with { Pending = [.. stillPending] },
                    $"undeliverable to {string.Join(", ", stillPending)} after "
                    + $"{entry.Attempts} attempts");
                entries.RemoveAt(index);

                foreach (var receiver in stillPending)
                {
                    blockedTargets.Remove(receiver);
                }

                continue;
            }

            entries[index] = serializer.SerializeToArray(
                entry with { Pending = [.. stillPending] });
            index++;
        }

        FlushOutcomes();

        await CommitAsync(cancellationToken)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        StopDrainingWhenEmpty();
    }

    private void FlushOutcomes()
    {
        foreach (var (cause, outcome) in _outcomes)
        {
            neuron.StageIncomingOutcome(outcome, cause);
        }

        _outcomes.Clear();
    }

    private void StageOutcome(
        SynapseDelivery delivery,
        NeuronId receiver,
        RouteOutcomeKind kind,
        string reason)
    {
        if (NeuronMessagePipeline.IsOutcome(delivery.Synapse))
        {
            return;
        }

        var alias = SynapseAlias.Of(delivery.Synapse.GetType()) ?? delivery.Synapse.GetType().Name;
        _outcomes.Add((delivery, RouteOutcome.For(delivery, alias, receiver, kind, reason)));
    }

    private bool Exhausted(OutboxEntry entry)
        => entry.Attempts >= DeliveryPolicy.MaximumAttempts
            || neuron.NeuronTimeProvider.GetUtcNow() - entry.Delivery.Timestamp
                > DeliveryPolicy.RetryHorizon;

    private async Task<bool> TryDeliverAsync(
        OutboxEntry entry,
        NeuronId receiver,
        CancellationToken drainToken)
    {
        DeliveryPolicy.CarryDepth(entry.Depth);

        using var attemptCts = drainToken.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(drainToken)
            : new CancellationTokenSource();
        attemptCts.CancelAfter(DeliveryPolicy.DeliveryAttemptTimeout);
        var attemptToken = attemptCts.Token;

        try
        {
            if (receiver == neuron.Id)
            {
                await neuron.Deliver(entry.Delivery, attemptToken)
                    .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            }
            else
            {
                await neuron.NeuronGrainFactory
                    .GetGrain<INeuron>(receiver.ToGrainId())
                    .Deliver(entry.Delivery, attemptToken)
                    .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            }

            return true;
        }
        // An attempt timeout is a retry, not a settled outcome, so it is caught first.
        catch (OperationCanceledException) when (attemptToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception failure) when (NeuronDeliveryMemory.Settles(failure))
        {
            Record("refused", entry.Delivery, receiver, failure.Message);
            StageOutcome(entry.Delivery, receiver, RouteOutcomeKind.Refused, failure.Message);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void Abandon(OutboxEntry entry, string reason)
    {
        foreach (var receiver in entry.Pending)
        {
            Record("abandoned", entry.Delivery, receiver, reason);
        }

        // One outcome per abandoned entry, naming every receiver it never reached.
        StageOutcome(
            entry.Delivery,
            entry.Pending[0],
            RouteOutcomeKind.Abandoned,
            $"{reason} (receivers: {string.Join(", ", entry.Pending)})");
    }

    private static void Record(
        string outcome,
        SynapseDelivery delivery,
        NeuronId receiver,
        string reason)
    {
        using var recorded = SynapseTelemetry.Source.StartActivity(outcome);

        recorded?.SetTag(SynapseTelemetry.ReceiverTag, receiver.ToString());
        recorded?.SetTag(SynapseTelemetry.SynapseTag, delivery.Synapse.GetType().Name);
        recorded?.SetTag(SynapseTelemetry.CorrelationTag, delivery.CorrelationId.ToString());
        recorded?.SetStatus(ActivityStatusCode.Error, reason);
    }
}
