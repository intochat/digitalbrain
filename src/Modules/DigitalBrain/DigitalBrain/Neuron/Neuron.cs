using System.Text.Json.Serialization.Metadata;
using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Abstractions.Synapses;
using Orleans.Journaling;
using Orleans.Runtime;

namespace DigitalBrain.Core;

public abstract class Neuron : DurableGrain, INeuron, INeuronInbox, IRemindable
{
    public const int MaxSignalTypesPerNeuron = 256;

    private const string ReactionSaveRule = "a reaction saves once, at the end. Anything after the first save is unreachable on a retry because the applied marker skips it.";

    private readonly NeuronActivationComponents _components;
    private readonly PersistenceFence _fence;
    private readonly CancellationTokenSource _activation = new();
    private readonly RetryScheduler _retry;
    private (SignalId Id, CancellationTokenSource Cancellation)? _reacting;
    private readonly List<SignalDelivery> _turnWork = [];
    private bool _reactionSaved;

    protected Neuron(NeuronRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        _components = runtime.Bind(ServiceProvider, Id);
        _fence = new PersistenceFence(Id, StateManager, _activation.Token,
            () => _components.CommandOutcomes.Reconcile(_components.Commands, TimeProvider.GetUtcNow()),
            DeactivateOnIdle, _components);
        _retry = new RetryScheduler(this, _ => ((INeuronInbox)this).Drain(), _components.Options.RetryReminderPeriod,
            () => _components.Pending.Count > 0 || HasStoredAnnouncements, _activation.Token);
    }

    public NeuronId Id => NeuronId.FromGrainId(this.GetGrainId());

    protected TimeProvider TimeProvider => _components.Clock;

    protected internal Task PersistAsync() => _fence.PersistAsync();

    protected new Task WriteStateAsync(CancellationToken cancellationToken = default)
        => throw new InvalidOperationException($"Neuron '{Id}' must call PersistAsync to write through the persistence fence.");

    internal Task GuardActivationAsync() => _fence.GuardAsync();

    private protected void Guard() => _fence.Guard();

    protected ReactionContext? ReactionContext { get; private set; }

    internal ReactionContext? TurnReaction
    {
        get => ReactionContext;
        set => ReactionContext = value;
    }

    internal CommandId? ExecutingCommand => (ReactionContext as CommandReaction)?.Command;

    internal bool HasPendingRoom => _components.Pending.HasRoomFor(0);

    internal Task DiscardStagedChangesAsync(Exception cause) => _fence.DiscardStagedChangesAsync(cause);

    internal void AdmitTurnWork()
    {
        if (_turnWork.Count == 0)
        {
            return;
        }

        if (!_components.Pending.HasRoomFor(_turnWork.Count))
        {
            _turnWork.Clear();
            throw new NeuronBusyException(
                $"Neuron '{Id}' cannot admit this turn's scheduled work: an interleaving Deliver can fill Pending between Schedule and the final flush. Retry the whole turn after pending work finishes.");
        }

        foreach (var delivery in _turnWork)
        {
            AdmitAndStageDelivery(delivery);
        }

        _turnWork.Clear();
    }

    internal Task WakeTurnWorkAsync()
    {
        return _components.Pending.Count == 0 ? Task.CompletedTask : EnsureReminderAndWakeAsync();

        async Task EnsureReminderAndWakeAsync()
        {
            await _retry.EnsureReminderAsync().ConfigureAwait(true);
            Wake();
        }
    }

    public sealed override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await base.OnActivateAsync(cancellationToken).ConfigureAwait(true);
        _components.NoteReloaded();
        _fence.NoteStoredState(StorageHoldsState());
        if (_components.CommandOutcomes.Reconcile(_components.Commands, TimeProvider.GetUtcNow()))
        {
            await PersistAsync().ConfigureAwait(true);
        }

        await OnNeuronActivatedAsync(cancellationToken).ConfigureAwait(true);

        // Pending entries and stored announcements survive deactivation: resume the drain for both.
        if (_components.Pending.Peek() is not null || HasStoredAnnouncements)
        {
            await _retry.EnsureReminderAsync().ConfigureAwait(true);
            Wake();
        }
    }

    private bool StorageHoldsState()
        => _components.IncomingNextSequence > 1
            || _components.OutgoingNextSequence > 1
            || _components.Commands.LastSequence > 0
            || _components.Synapses.All().Count > 0
            || _components.Pending.Count > 0
            || _components.Latest.Count > 0;

    public sealed override async Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
    {
        _retry.Suspend();
        await _activation.CancelAsync().ConfigureAwait(true);
        try
        {
            await base.OnDeactivateAsync(reason, cancellationToken).ConfigureAwait(true);
        }
        finally
        {
            _retry.Dispose();
            _activation.Dispose();
        }
    }

    protected virtual Task OnNeuronActivatedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    protected virtual Task ReceiveAsync(SignalDelivery delivery, CancellationToken cancellationToken) => Task.CompletedTask;

    protected T? Body<T>(SignalDelivery delivery, JsonTypeInfo<T> json) where T : class
        => delivery.Body(json);

    private protected virtual bool HasStoredAnnouncements => false;

    private protected virtual bool HasBufferedAnnouncements => false;

    private protected void NoteSnapshotSaved() => _reactionSaved = ReactionContext is DeliveryReaction;

    private protected void RequireBeforeReactionSave()
    {
        if (_reactionSaved)
        {
            throw new InvalidOperationException(
                $"Neuron '{Id}' cannot save again; {ReactionSaveRule}");
        }
    }

    private protected virtual bool IsAppliedBy(SignalId delivery) => false;

    private protected virtual void DiscardBufferedAnnouncements() { }

    private protected virtual Task<bool> DrainAnnouncementsAsync(CancellationToken cancellationToken) => Task.FromResult(false);

    public Task<FireOutcome> Fire(Signal signal, NeuronId? to, CorrelationId? correlation, CancellationToken cancellationToken = default)
    {
        Guard();
        if (ExecutingCommand is { } id)
        {
            throw new InvalidOperationException(
                $"Neuron '{Id}' cannot fire while executing command '{id}': fire from a reaction, not a command. Schedule the work instead.");
        }

        return FireCoreAsync(signal, to, correlation, cancellationToken);
    }

    public async Task Connect(NeuronId target, string signalType)
    {
        Guard();
        ArgumentException.ThrowIfNullOrWhiteSpace(signalType);
        if (_components.Synapses.Connect(target, signalType))
        {
            await PersistAsync().ConfigureAwait(true);
        }
    }

    public async Task Disconnect(NeuronId target, string signalType)
    {
        Guard();
        ArgumentException.ThrowIfNullOrWhiteSpace(signalType);
        if (_components.Synapses.Disconnect(target, signalType))
        {
            await PersistAsync().ConfigureAwait(true);
        }
    }

    public async Task<DeliveryAdmission> Deliver(SignalDelivery delivery, CancellationToken cancellationToken = default)
    {
        Guard();
        ArgumentNullException.ThrowIfNull(delivery);
        cancellationToken.ThrowIfCancellationRequested();

        var admission = _components.Pending.Classify(delivery);
        if (admission != DeliveryAdmission.Accepted)
        {
            return admission;
        }

        RequireSignalTypeCapacity(delivery.Signal.Type);
        _components.Pending.Admit(delivery);
        StageAdmitted(delivery);
        await PersistAsync().ConfigureAwait(true);
        // Register after persistence so work that never committed cannot leave an orphan reminder row.
        await _retry.EnsureReminderAsync().ConfigureAwait(true);

        Wake();
        return DeliveryAdmission.Accepted;
    }

    protected SignalId Schedule(Signal signal, CorrelationId? correlation = null)
    {
        ArgumentNullException.ThrowIfNull(signal);
        if (_reactionSaved)
        {
            throw new InvalidOperationException(
                $"Neuron '{Id}' must schedule and announce before the save; {ReactionSaveRule}");
        }

        signal = Signal.Create(signal.Type, signal.Body);
        RequireSignalTypeCapacity(signal.Type);
        var delivery = SignalDelivery.Create(signal, Id, _components.IncomingNextSequence, TimeProvider, (ReactionContext as DeliveryReaction)?.Delivery, correlation);
        if (!_components.Pending.HasRoomFor(_turnWork.Count))
        {
            throw new NeuronBusyException($"Neuron '{Id}' already holds {PendingWork.MaxPending} pending signals. Retry after pending work finishes.");
        }

        if (ReactionContext is not null)
        {
            _turnWork.Add(delivery);
            if (ReactionContext is CommandReaction commandReaction)
            {
                ReactionContext = commandReaction with { ScheduledWork = [.. commandReaction.ScheduledWork, delivery.SignalId] };
            }

            return delivery.SignalId;
        }

        // Without a reaction context there is no turn to flush buffered work, so admit and wake immediately.
        AdmitAndStageDelivery(delivery);
        Wake();
        return delivery.SignalId;
    }

    private void AdmitAndStageDelivery(SignalDelivery delivery)
    {
        delivery = delivery with { Sequence = _components.IncomingNextSequence };
        if (_components.Pending.Classify(delivery) == DeliveryAdmission.Duplicate)
        {
            throw new InvalidOperationException($"Freshly minted scheduled signal id '{delivery.SignalId}' must never be a duplicate.");
        }

        _components.Pending.Admit(delivery);
        StageAdmitted(delivery);
    }

    private void StageAdmitted(SignalDelivery delivery)
    {
        _components.AppendIncoming(delivery);
        _components.Latest[delivery.Signal.Type] = delivery;
    }

    public async Task CancelReaction(SignalId pending)
    {
        Guard();
        if (!_components.Pending.Cancel(pending))
        {
            return;
        }

        await PersistAsync().ConfigureAwait(true);
        if (_reacting is { } reacting && reacting.Id == pending)
        {
            await reacting.Cancellation.CancelAsync().ConfigureAwait(true);
        }
    }

    private void RequireSignalTypeCapacity(string signalType)
    {
        if (_components.Latest.Count >= MaxSignalTypesPerNeuron && !_components.Latest.ContainsKey(signalType))
        {
            throw new SignalRejectedException(
                $"Neuron '{Id}' already remembers {MaxSignalTypesPerNeuron} signal types. "
                + "Type names are vocabulary such as 'Note'; put identity in the neuron name.");
        }
    }

    async Task INeuronInbox.Drain()
    {
        Guard();
        RequestContext.Clear();
        if (_components.Pending.Peek() is not { } head)
        {
            await FinishDrainAsync().ConfigureAwait(true);
            return;
        }

        var delivery = head.Delivery;
        await _retry.EnsureReminderAsync().ConfigureAwait(true);

        if (_components.Pending.IsCancelled(delivery.SignalId) || IsAppliedBy(delivery.SignalId))
        {
            // Cancelled work, or a head whose snapshot already committed: finish it without reacting.
            _components.Pending.CompleteHead(head);
            await PersistAsync().ConfigureAwait(true);
            await FinishDrainAsync().ConfigureAwait(true);
            return;
        }

        RequestContext.Set(NeuronRequestKeys.Caller, delivery.Source.ToString());
        RequestContext.Set(NeuronRequestKeys.Correlation, delivery.CorrelationId.ToString());
        RequestContext.Set(NeuronRequestKeys.Causation, delivery.SignalId.ToString());

        var previous = ReactionContext;
        var previousReacting = _reacting;
        using var reaction = CancellationTokenSource.CreateLinkedTokenSource(_activation.Token);
        ReactionContext = new DeliveryReaction(delivery);
        _reacting = (delivery.SignalId, reaction);
        try
        {
            try
            {
                await ReceiveAsync(delivery, reaction.Token).ConfigureAwait(true);
                if (HasBufferedAnnouncements)
                {
                    throw new InvalidOperationException(
                        $"Neuron '{Id}' announced without saving: announcements are saved with the snapshot.");
                }

                AdmitTurnWork();
            }
            catch (OperationCanceledException) when (_activation.Token.IsCancellationRequested)
            {
                // Deactivation is not a reaction failure and must not re-arm the suspended retry timer.
                return;
            }
            catch (Exception failure)
            {
                if (!_components.Pending.IsCancelled(delivery.SignalId))
                {
                    await _fence.DiscardStagedChangesAsync(failure).ConfigureAwait(true);
                    _retry.ArmTimer();
                    return;
                }
            }

            _components.Pending.CompleteHead(head);
            await PersistAsync().ConfigureAwait(true);
        }
        finally
        {
            // A failed or cancelled attempt drops whatever it scheduled.
            _turnWork.Clear();
            _reactionSaved = false;
            DiscardBufferedAnnouncements();
            ReactionContext = previous;
            _reacting = previousReacting;
        }

        await FinishDrainAsync().ConfigureAwait(true);
    }

    private async Task FinishDrainAsync()
    {
        var remaining = await TryDrainAnnouncementsAsync().ConfigureAwait(true);
        if (_activation.IsCancellationRequested)
        {
            return;
        }

        await _retry.AfterDrainAsync(remaining).ConfigureAwait(true);
        if (remaining)
        {
            _retry.ArmTimer();
        }

        // Only queued entries wake immediately; busy announcements wait for the retry timer.
        if (_components.Pending.Peek() is not null)
        {
            Wake();
        }
    }

    private async Task<bool> TryDrainAnnouncementsAsync()
    {
        try
        {
            return await DrainAnnouncementsAsync(_activation.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (_activation.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception)
        {
            return true;
        }
    }

    public Task<IReadOnlyList<SignalDelivery>> ReadState()
    {
        Guard();
        return Task.FromResult<IReadOnlyList<SignalDelivery>>(
            [.. _components.Latest.Values.OrderBy(d => d.Signal.Type, StringComparer.Ordinal)]);
    }

    public Task<int> ReadPendingCount()
    {
        Guard();
        return Task.FromResult(_components.Pending.Count);
    }

    public Task<IReadOnlyList<Synapse>> ReadSynapses()
    {
        Guard();
        return Task.FromResult(_components.Synapses.All());
    }

    public Task<JournalRead> ReadJournal(JournalKind kind, long afterSequence)
    {
        Guard();
        return Task.FromResult(_components.Read(kind, afterSequence));
    }

    public Task<CommandJournalRead> ReadCommands(long afterSequence)
    {
        Guard();
        return Task.FromResult(_components.Commands.Read(afterSequence));
    }

    protected async Task<TResult> ExecuteCommandAsync<TArguments, TResult>(
        CommandDescriptor command, TArguments arguments,
        JsonTypeInfo<TArguments> argumentsJson, JsonTypeInfo<TResult> resultJson,
        Func<TArguments, TResult> execute) where TArguments : Command
    {
        Guard();
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(execute);
        try
        {
            return await _components.Execution.RunAsync(this, command, arguments, argumentsJson, resultJson, execute).ConfigureAwait(true);
        }
        finally
        {
            _turnWork.Clear();
        }
    }

    internal Task<FireOutcome> FireAnnouncementAsync(Announcement announcement, CancellationToken cancellationToken)
        => FireCoreAsync(announcement.Signal, announcement.To, announcement.Correlation, cancellationToken,
            announcement.Id, announcement.Causation);

    private async Task<FireOutcome> FireCoreAsync(
        Signal signal, NeuronId? to, CorrelationId? correlation, CancellationToken cancellationToken,
        SignalId? fixedId = null, SignalId? fixedCausation = null)
    {
        ArgumentNullException.ThrowIfNull(signal);
        cancellationToken.ThrowIfCancellationRequested();

        // Re-validate: a Signal deserialized from the wire may bypass Create. Keep the
        // normalized instance — Create fills a blank body with "{}".
        signal = Signal.Create(signal.Type, signal.Body);

        if (to is { } target && target == Id)
        {
            throw new SignalRejectedException($"Neuron '{Id}' cannot fire at itself.");
        }

        // The directed edge is created before the journal entry so anatomy and traffic agree.
        if (to is { } single)
        {
            _components.Synapses.Connect(single, signal.Type);
        }

        var delivery = SignalDelivery.Create(signal, Id, _components.OutgoingNextSequence, TimeProvider, (ReactionContext as DeliveryReaction)?.Delivery, correlation);
        if (fixedId is { } announcementId)
        {
            delivery = delivery with { SignalId = announcementId, CausationId = fixedCausation };
        }

        // A re-fire is the same signal, so the journal keeps one entry for it.
        if (fixedId is null || !_components.RetainsOutgoing(delivery.SignalId))
        {
            _components.AppendOutgoing(delivery);
        }
        await PersistAsync().ConfigureAwait(true);

        var targets = to is { } one
            ? [one]
            : _components.Synapses.ForType(signal.Type).Select(s => s.Target).Where(t => t != Id).Distinct().ToArray();

        var delivered = 0;
        var busy = 0;
        List<Exception>? failures = null;
        foreach (var receiver in targets)
        {
            try
            {
                var admission = await GrainFactory.GetGrain<INeuron>(receiver.ToGrainId())
                    .Deliver(delivery, cancellationToken)
                    .ConfigureAwait(true);
                if (admission == DeliveryAdmission.Accepted)
                {
                    delivered++;
                }
                else if (admission == DeliveryAdmission.Busy)
                {
                    busy++;
                }
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                (failures ??= []).Add(error);
            }
        }

        if (failures is not null)
        {
            throw new AggregateException(
                $"Delivery of '{signal.Type}' from '{Id}' failed for {failures.Count} of {targets.Length} receivers.",
                failures);
        }

        return new FireOutcome(delivery.SignalId, delivery.CorrelationId, delivered, busy);
    }

    Task IRemindable.ReceiveReminder(string reminderName, TickStatus status)
    {
        if (reminderName != RetryScheduler.ReminderName)
        {
            return Task.CompletedTask;
        }

        _retry.NoteTick();
        Wake();
        return Task.CompletedTask;
    }

    private void Wake() => GrainFactory.GetGrain<INeuronInbox>(this.GetGrainId()).Drain().Ignore();
}
