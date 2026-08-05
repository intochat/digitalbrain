using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace DigitalBrain;

public abstract partial class Neuron : Neuron.IDrainEntry
{
    private readonly SortedSet<long> unsettled = [];
    private readonly Dictionary<long, RehydratedFact> rehydrated = [];
    private IGrainTimer? drainTimer;
    private bool wakeupArmed;

    [Alias("db.drain")]
    internal interface IDrainEntry : IGrainWithStringKey
    {
        [Alias("drain")]
        Task DrainAsync();
    }

    async Task IDrainEntry.DrainAsync()
    {
        RefusePoisoned();
        wakeupArmed = true;
        if (journal.PruneWatermarks(clock.GetUtcNow(), DeliveryPolicy.WatermarkRetention))
        {
            await CommitCoreBatchAsync(deliverable: false);
        }

        using var drainLifecycle = new CancellationTokenSource();
        await DrainOutboxAsync(drainLifecycle.Token);
    }

    private async partial Task ResumeDispatchAsync()
    {
        RebuildUnsettledIndex();
        SyncScheduleTimers();
        if (unsettled.Count > 0 || journal.HasAskPins || journal.HasSchedules)
        {
            await ArmWakeupAsync();
        }

        ScheduleDrain();
    }

    private async partial ValueTask ArmWakeupAsync()
    {
        if (wakeupArmed)
        {
            return;
        }

        await WakeupGrain().ArmAsync();
        wakeupArmed = true;
    }

    private partial void ScheduleDrain()
    {
        if (drainTimer is not null || (unsettled.Count == 0 && !journal.HasAskPins))
        {
            return;
        }

        drainTimer = this.RegisterGrainTimer(
            static (self, cancellationToken) => self.DrainOutboxAsync(cancellationToken),
            this,
            new GrainTimerCreationOptions
            {
                DueTime = DeliveryPolicy.RetryInterval,
                Period = DeliveryPolicy.RetryInterval,
            });
    }

    private async Task DrainOutboxAsync(CancellationToken cancellationToken)
    {
        if (poisoned)
        {
            return;
        }

        try
        {
            await DrainPassAsync(cancellationToken);
        }
        catch
        {
            Poison();
            throw;
        }
    }

    private async Task DrainPassAsync(CancellationToken cancellationToken)
    {
        await ExpireAsksAsync();

        var blockedTargets = new HashSet<NeuronId>();
        var dirty = false;

        foreach (var position in unsettled.ToArray())
        {
            if (journal.EntryAt(position) is not { To: { } snapshot } entry)
            {
                unsettled.Remove(position);
                continue;
            }

            var progress = journal.ProgressOf(position);
            var pending = progress?.Pending ?? snapshot;
            var attempts = progress?.Attempts ?? 0;

            if (Array.TrueForAll(pending, receiver => blockedTargets.Contains(receiver.ToNeuronId())))
            {
                continue;
            }

            attempts++;
            var fact = RehydratedAt(position, entry);
            var stillPending = new List<NeuronIdEntry>();
            var refused = new List<(NeuronIdEntry Receiver, string Reason)>();

            foreach (var receiver in pending)
            {
                var receiverId = receiver.ToNeuronId();
                if (blockedTargets.Contains(receiverId))
                {
                    stillPending.Add(receiver);
                    continue;
                }

                if (fact.Failure is { } undeliverable)
                {
                    refused.Add((receiver, undeliverable));
                    continue;
                }

                if (receiverId != Id && !catalog.TryGetNeuronType(receiver.Kind, out _))
                {
                    refused.Add((receiver, $"neuron kind '{receiver.Kind}' is not in the running catalog"));
                    continue;
                }

                var attempt = await TryDeliverAsync(entry, fact.Fact!, receiver, receiverId, cancellationToken);
                if (attempt.Refusal is { } refusal)
                {
                    refused.Add((receiver, refusal));
                }
                else if (!attempt.Settled)
                {
                    stillPending.Add(receiver);
                    blockedTargets.Add(receiverId);
                }
            }

            var exhausted = attempts >= DeliveryPolicy.MaximumAttempts
                || clock.GetUtcNow() - entry.At > DeliveryPolicy.RetryHorizon;

            if (refused.Count > 0 || (exhausted && stillPending.Count > 0))
            {
                if (exhausted)
                {
                    refused.AddRange(stillPending.Select(receiver
                        => (receiver, $"undeliverable after {attempts} attempts within the retry horizon")));
                    stillPending.Clear();
                }

                var terminalAt = clock.GetUtcNow();
                var failedRef = new SynapseRefEntry(Id.Kind, Id.Name, position);
                var deliverable = false;
                foreach (var (receiver, reason) in refused)
                {
                    deliverable |= StageCoreSaid(
                        new DeliveryFailed(new SynapseRef(Id, position), receiver.ToNeuronId(), reason, attempts),
                        failedRef,
                        terminalAt);
                }

                SettleProgress(position, stillPending, attempts);

                await CommitCoreBatchAsync(deliverable);
                dirty = false;
                foreach (var (receiver, _) in refused)
                {
                    blockedTargets.Remove(receiver.ToNeuronId());
                }
            }
            else
            {
                SettleProgress(position, stillPending, attempts);
                dirty = true;
            }
        }

        dirty |= AdvanceCursor();
        if (dirty)
        {
            await CommitCoreBatchAsync(deliverable: false);
        }

        if (unsettled.Count == 0 && !journal.HasAskPins)
        {
            drainTimer?.Dispose();
            drainTimer = null;
            await DisarmWakeupWhenIdleAsync();
        }
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Any failure that is not a classified permanent refusal keeps the receiver pending under the bounded retry; letting it escape a swallowing timer would abandon the delivery guarantee silently.")]
    private async Task<DeliveryAttempt> TryDeliverAsync(
        JournalEntry entry,
        Synapse fact,
        NeuronIdEntry receiver,
        NeuronId receiverId,
        CancellationToken drainToken)
    {
        var envelope = entry.ToEnvelope(Id);
        var factType = fact.GetType();
        var questionRoute = receiver.IsQuestionDeliver;

        using var attemptSource = drainToken.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(drainToken)
            : new CancellationTokenSource();
        attemptSource.CancelAfter(DeliveryPolicy.DeliveryAttemptTimeout);
        var attemptToken = attemptSource.Token;

        try
        {
            if (receiverId == Id)
            {
                await DeliverToSelfAsync(fact, envelope, questionRoute, attemptToken);
            }
            else
            {
                var transport = base.GrainFactory.GetGrain<ITransport>(AddressOf(receiverId));
                StageOutboundDelivery(envelope);
                await (questionRoute
                    ? WireQuestionDelivererFor(factType)
                    : WireDelivererFor(factType))(transport, fact, attemptToken);
            }

            return DeliveryAttempt.Delivered;
        }
        catch (UnhandledFactException refusal)
        {
            return DeliveryAttempt.Terminal(refusal.Message);
        }
        catch (OperationCanceledException) when (attemptToken.IsCancellationRequested)
        {
            return DeliveryAttempt.Transient;
        }
        catch (Exception failure) when (IsUnresolvableGrainKind(failure))
        {
            return DeliveryAttempt.Terminal(failure.Message);
        }
        catch (Exception)
        {
            return DeliveryAttempt.Transient;
        }
    }

    private static bool IsUnresolvableGrainKind(Exception failure)
    {
        for (Exception? cause = failure; cause is not null; cause = cause.InnerException)
        {
            if (cause is KeyNotFoundException && cause.Message.Contains("grain", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private RehydratedFact RehydratedAt(long position, JournalEntry entry)
    {
        if (rehydrated.TryGetValue(position, out var cached))
        {
            return cached;
        }

        RehydratedFact decoded;
        try
        {
            decoded = codec.DecodeFact(entry.Kind, entry.Body) is { } fact
                ? new RehydratedFact(fact, Failure: null)
                : new RehydratedFact(Fact: null, $"kind '{entry.Kind}' is not in the running catalog");
        }
        catch (JsonException failure)
        {
            decoded = new RehydratedFact(Fact: null, $"the journaled '{entry.Kind}' body does not rehydrate: {failure.Message}");
        }

        rehydrated[position] = decoded;
        return decoded;
    }

    private void SettleProgress(long position, List<NeuronIdEntry> pending, int attempts)
    {
        journal.SetProgress(position, new DeliveryProgress([.. pending], attempts));
        if (pending.Count == 0)
        {
            unsettled.Remove(position);
            rehydrated.Remove(position);
        }
    }

    private bool AdvanceCursor()
    {
        var settledThrough = unsettled.Count > 0 ? unsettled.Min - 1 : journal.LastCommitted;
        var current = journal.Cursor;
        if (settledThrough <= current)
        {
            return false;
        }

        for (var position = current + 1; position <= settledThrough; position++)
        {
            journal.ClearProgress(position);
            rehydrated.Remove(position);
        }

        journal.Cursor = settledThrough;
        return true;
    }

    private void RebuildUnsettledIndex()
    {
        unsettled.Clear();
        rehydrated.Clear();
        for (var position = journal.Cursor + 1; position <= journal.LastCommitted; position++)
        {
            if (journal.EntryAt(position) is { Entry: JournalEntry.Said, To.Length: > 0 }
                && journal.ProgressOf(position) is not { Pending.Length: 0 })
            {
                unsettled.Add(position);
            }
        }
    }

    private void IndexNewlyCommitted(long fromExclusive)
    {
        for (var position = fromExclusive + 1; position <= journal.LastCommitted; position++)
        {
            if (journal.EntryAt(position) is { Entry: JournalEntry.Said, To.Length: > 0 })
            {
                unsettled.Add(position);
            }
        }
    }

    private async Task CommitCoreBatchAsync(bool deliverable)
    {
        var before = journal.LastCommitted;
        try
        {
            if (deliverable || journal.HasAskPins || journal.HasSchedules)
            {
                await ArmWakeupAsync();
            }

            await base.WriteStateAsync(CancellationToken.None);
        }
        catch
        {
            Poison();
            throw;
        }

        journal.MarkCommitted();
        IndexNewlyCommitted(before);
        ScheduleDrain();
    }

    private bool StageCoreSaid(Synapse fact, SynapseRefEntry cause, DateTimeOffset now, NeuronId? directedTo = null)
        => StageSaid(
            StagedFor(fact) with { DirectedTo = directedTo },
            cause,
            now,
            replyTo: null,
            journal.OpenAsksSnapshot());

    private async ValueTask DisarmWakeupWhenIdleAsync()
    {
        if (!wakeupArmed || unsettled.Count > 0 || journal.HasAskPins || journal.HasSchedules)
        {
            return;
        }

        await WakeupGrain().DisarmAsync();
        wakeupArmed = false;
    }

    private IOutboxWakeup WakeupGrain() => base.GrainFactory.GetGrain<IOutboxWakeup>(Id.ToString());

    private readonly record struct RehydratedFact(Synapse? Fact, string? Failure);

    private readonly record struct DeliveryAttempt(bool Settled, string? Refusal)
    {
        internal static DeliveryAttempt Delivered { get; } = new(Settled: true, Refusal: null);

        internal static DeliveryAttempt Transient { get; } = new(Settled: false, Refusal: null);

        internal static DeliveryAttempt Terminal(string reason) => new(Settled: true, reason);
    }
}
