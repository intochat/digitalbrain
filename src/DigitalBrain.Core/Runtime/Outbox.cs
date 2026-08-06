using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace DigitalBrain;

internal sealed class Outbox(
    Neuron owner,
    Journal journal,
    Router router,
    SpeechStager stager,
    ISynapseCodec codec,
    IEnvelopeCarrier envelopes)
{
    private IGrainTimer? retryTimer;
    private bool wakeupArmed;

    internal async Task ResumeAsync()
    {
        if (!journal.HasPending())
        {
            await StopAsync();
            return;
        }

        await ArmWakeupAsync();
        EnsureRetryTimer();
    }

    internal Task PrepareCommitAsync()
        => journal.HasUncommittedPending() ? ArmWakeupAsync() : Task.CompletedTask;

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

            var pending = journal.ProgressOf(entry.Position)?.Pending ?? entry.To!;
            var attempts = (journal.ProgressOf(entry.Position)?.Attempts ?? 0) + 1;
            var remaining = new List<DeliveryTarget>();
            var terminal = new List<(DeliveryTarget Receiver, string Reason)>();

            foreach (var receiver in pending)
            {
                var result = await TryDeliverAsync(entry, receiver, cancellationToken);
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
                || TimeProvider.System.GetUtcNow() - entry.At >= DeliveryPolicy.RetryHorizon)
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

            var cause = new SynapseRefEntry(owner.Id.Kind, owner.Id.Name, entry.Position);
            foreach (var (receiver, reason) in terminal)
            {
                _ = stager.Stage(
                    owner.Id,
                    new DeliveryFailed(new SynapseRef(owner.Id, entry.Position), receiver.ToNeuronId(), reason, attempts),
                    cause,
                    TimeProvider.System.GetUtcNow());
            }

            await owner.CommitCoreAsync();
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
            await owner.CoreGrainFactory.GetGrain<IOutboxWakeup>(OutboxWakeup.AddressOf(owner.Id)).ArmAsync();
            wakeupArmed = true;
        }
    }

    private async Task StopAsync()
    {
        retryTimer?.Dispose();
        retryTimer = null;
        await owner.CoreGrainFactory.GetGrain<IOutboxWakeup>(OutboxWakeup.AddressOf(owner.Id)).DisarmAsync();
        wakeupArmed = false;
    }

    private void EnsureRetryTimer()
    {
        retryTimer ??= owner.RegisterOutboxTimer(
            static (outbox, cancellationToken) => outbox.DrainAsync(cancellationToken), this);
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "A remote receiver failure is transient until the durable retry bound is reached.")]
    private async Task<DeliveryResult> TryDeliverAsync(
        JournalEntry entry, DeliveryTarget target, CancellationToken cancellationToken)
    {
        var receiver = target.ToNeuronId();
        if (!router.IsKnown(receiver))
        {
            return DeliveryResult.Terminal($"neuron kind '{receiver.Kind}' is absent from the catalog");
        }

        Synapse? fact;
        try
        {
            fact = codec.DecodeFact(entry.Kind, entry.Body);
        }
        catch (JsonException failure)
        {
            return DeliveryResult.Terminal(failure.Message);
        }

        if (fact is null)
        {
            return DeliveryResult.Terminal($"fact kind '{entry.Kind}' is absent from the catalog");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(DeliveryPolicy.AttemptTimeout);
        try
        {
            envelopes.Write(entry.ToEnvelope(owner.Id));
            var transport = owner.CoreGrainFactory.GetGrain<Neuron.ITransport>(Neuron.AddressOf(receiver));
            await Neuron.WireDelivererFor(fact.GetType())(transport, fact, timeout.Token);
            return DeliveryResult.Success;
        }
        catch (InvalidOperationException failure) when (
            failure.Message.StartsWith(Neuron.DeliveryRejectedPrefix, StringComparison.Ordinal))
        {
            return DeliveryResult.Terminal(failure.Message);
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

    private readonly record struct DeliveryResult(bool Delivered, string? Reason)
    {
        internal static DeliveryResult Success { get; } = new(true, null);

        internal static DeliveryResult Transient { get; } = new(false, null);

        internal static DeliveryResult Terminal(string reason) => new(false, reason);
    }
}
