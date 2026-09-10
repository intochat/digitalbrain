using System.Text.Json.Serialization.Metadata;
using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Abstractions.Synapses;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans.Concurrency;
using Orleans.Journaling;
using Orleans.Runtime;

namespace DigitalBrain.Core;

// A durable actor with one receive slot. Owns its synapses, three bounded journals, and the
// latest signal of each type it received. Fire travels along synapses; nothing else routes.
public abstract class Neuron : DurableGrain, INeuron, INeuronInbox, IRemindable, ICommandHost
{
    // Latest-per-type is keyed by type name, so a caller putting identity in the type would
    // grow it without bound. The cap turns that mistake into one sentence of advice.
    public const int MaxSignalTypesPerNeuron = 256;

    private readonly NeuronActivationComponents _components;
    private readonly PersistenceFence _fence;
    private readonly DescriptorTable _descriptors;

    private readonly CancellationTokenSource _activation = new();
    private readonly RetryScheduler _retry;
    private (SignalId Id, CancellationTokenSource Cancellation)? _reacting;
    private readonly List<SignalDelivery> _commandWork = [];

    protected Neuron(NeuronRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        _descriptors = ServiceProvider.GetRequiredService<DescriptorTable>();
        _components = runtime.Bind(ServiceProvider, Id);
        _fence = new PersistenceFence(Id, StateManager, _activation.Token,
            () => CommandReconciliation.Reconcile(_components.Commands, _components.Dedup, TimeProvider.GetUtcNow()),
            DeactivateOnIdle, _components);
        _retry = new RetryScheduler(this, _ => ((INeuronInbox)this).Drain(), _components.Options.RetryReminderPeriod,
            () => _components.Pending.Count > 0);
    }

    public NeuronId Id => NeuronId.FromGrainId(this.GetGrainId());

    protected TimeProvider TimeProvider => _components.Clock;

    // A durable write belongs to the activation, so request cancellation cannot interrupt it.
    protected Task PersistAsync() => _fence.PersistAsync();

    protected new Task WriteStateAsync(CancellationToken cancellationToken = default)
        => throw new InvalidOperationException($"Neuron '{Id}' must call PersistAsync to write through the persistence fence.");

    internal Task GuardActivationAsync() => _fence.GuardAsync();

    private protected void Guard() => _fence.Guard();

    protected ReactionContext? ReactionContext { get; private set; }

    internal CommandId? ExecutingCommand => (ReactionContext as CommandReaction)?.Command;

    NeuronId ICommandHost.Id => Id;

    ReactionContext? ICommandHost.ReactionContext
    {
        get => ReactionContext;
        set => ReactionContext = value;
    }

    Task ICommandHost.PersistAsync() => PersistAsync();

    Task ICommandHost.DiscardStagedChangesAsync(Exception cause) => _fence.DiscardStagedChangesAsync(cause);

    void ICommandHost.AdmitCommandWork()
    {
        foreach (var delivery in _commandWork)
        {
            AdmitAndStageDelivery(delivery);
        }

        _commandWork.Clear();
    }

    Task ICommandHost.WakeCommandWorkAsync()
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
        NeuronConcurrency.RequireSerializedTurns(GetType());
        await base.OnActivateAsync(cancellationToken).ConfigureAwait(true);
        _components.NoteReloaded();
        _fence.NoteStoredState(StorageHoldsState());
        if (CommandReconciliation.Reconcile(_components.Commands, _components.Dedup, TimeProvider.GetUtcNow()))
        {
            await PersistAsync().ConfigureAwait(true);
        }

        await OnNeuronActivatedAsync(cancellationToken).ConfigureAwait(true);

        // Entries accepted before the last deactivation are still pending: resume the drain.
        if (_components.Pending.Peek() is not null)
        {
            await _retry.EnsureReminderAsync().ConfigureAwait(true);
            Wake();
        }
    }

    private bool StorageHoldsState()
        => _components.Journals.IncomingNextSequence > 1
            || _components.Journals.OutgoingNextSequence > 1
            || _components.Commands.LastSequence > 0
            || _components.Synapses.All().Count > 0
            || _components.Pending.Count > 0
            || _components.Latest.Count > 0;

    // Shutting down cancels the reaction in flight. The pending head stays and
    // the next activation retries the entry.
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
            _activation.Dispose();
        }
    }

    protected virtual Task OnNeuronActivatedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    // Override to react. The default neuron does nothing: the signal is already journaled and remembered.
    protected virtual Task ReceiveAsync(SignalDelivery delivery, CancellationToken cancellationToken) => Task.CompletedTask;

    // ---- INeuron ----

    public Task<FireOutcome> Fire(Signal signal, NeuronId? to, CorrelationId? correlation, CancellationToken cancellationToken = default)
    {
        Guard();
        return FireAsync(signal, to, correlation, cancellationToken);
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

    // Commands buffer work until their terminal flush; reactions stage it now and register on the queued drain turn.
    protected SignalId Schedule(Signal signal, CorrelationId? correlation = null)
    {
        ArgumentNullException.ThrowIfNull(signal);
        signal = Signal.Create(signal.Type, signal.Body);
        RequireSignalTypeCapacity(signal.Type);
        var delivery = SignalDelivery.Create(signal, Id, _components.Journals.IncomingNextSequence, TimeProvider, (ReactionContext as DeliveryReaction)?.Delivery, correlation);
        if (!_components.Pending.HasRoomFor(_commandWork.Count))
        {
            throw new NeuronBusyException($"Neuron '{Id}' already holds {PendingWork.MaxPending} pending signals. Retry after pending work finishes.");
        }

        if (ReactionContext is CommandReaction commandReaction)
        {
            // Command work becomes visible only with its terminal record, never after an uncommitted attempt.
            _commandWork.Add(delivery);
            ReactionContext = commandReaction with { ScheduledWork = [.. commandReaction.ScheduledWork, delivery.SignalId] };
            return delivery.SignalId;
        }

        AdmitAndStageDelivery(delivery);
        Wake();
        return delivery.SignalId;
    }

    private void AdmitAndStageDelivery(SignalDelivery delivery)
    {
        delivery = delivery with { Sequence = _components.Journals.IncomingNextSequence };
        if (_components.Pending.Classify(delivery) != DeliveryAdmission.Accepted)
        {
            throw new InvalidOperationException($"Scheduled signal '{delivery.SignalId}' admission was already classified as Accepted; this state is unreachable.");
        }

        _components.Pending.Admit(delivery);
        StageAdmitted(delivery);
    }

    private void StageAdmitted(SignalDelivery delivery)
    {
        _components.Journals.AppendIncoming(delivery);
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

    // ---- INeuronInbox ----

    // Reacts to exactly one pending entry per call, then re-wakes if more remain, so that
    // accepts interleave with reactions and journal order is preserved.
    async Task INeuronInbox.Drain()
    {
        Guard();
        RequestContext.Clear();
        var delivery = _components.Pending.Peek();
        if (delivery is null)
        {
            await _retry.AfterDrainAsync().ConfigureAwait(true);
            return;
        }

        await _retry.EnsureReminderAsync().ConfigureAwait(true);

        if (_components.Pending.IsCancelled(delivery.SignalId))
        {
            _components.Pending.CompleteHead(delivery.SignalId);
            await PersistAsync().ConfigureAwait(true);
            await _retry.AfterDrainAsync().ConfigureAwait(true);
            Wake();
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
            }
            catch (OperationCanceledException) when (_activation.Token.IsCancellationRequested)
            {
                // Deactivation is not a reaction failure and must not re-arm the suspended retry timer.
                return;
            }
            catch (Exception failure)
            {
                if (_components.Pending.IsCancelled(delivery.SignalId))
                {
                    DrainTelemetry.Cancelled(Logger, Id, delivery.SignalId);
                    _components.Pending.CompleteHead(delivery.SignalId);
                    await PersistAsync().ConfigureAwait(true);
                    await _retry.AfterDrainAsync().ConfigureAwait(true);
                    Wake();
                }
                else
                {
                    DrainTelemetry.Failed(Logger, Id, delivery.SignalId, failure);
                    await _fence.DiscardStagedChangesAsync(failure).ConfigureAwait(true);
                    _retry.ArmTimer();
                }

                return;
            }

            _components.Pending.CompleteHead(delivery.SignalId);
            await PersistAsync().ConfigureAwait(true);
            await _retry.AfterDrainAsync().ConfigureAwait(true);
            Wake();
        }
        finally
        {
            ReactionContext = previous;
            _reacting = previousReacting;
        }
    }

    // ---- INeuron: the reads ----

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
        return Task.FromResult(_components.Journals.Read(kind, afterSequence));
    }

    public Task<CommandJournalRead> ReadCommands(long afterSequence)
    {
        Guard();
        return Task.FromResult(_components.Commands.Read(afterSequence));
    }

    // ---- for subclasses ----

    protected CommandDescriptor Descriptor(string methodAlias) => _descriptors.DescriptorFor(this.GetGrainId().Type, methodAlias);

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
            _commandWork.Clear();
        }
    }

    protected Task<FireOutcome> FireAsync(
        Signal signal,
        NeuronId? to = null,
        CorrelationId? correlation = null,
        CancellationToken cancellationToken = default)
    {
        if (ExecutingCommand is { } id)
        {
            throw new InvalidOperationException(
                $"Neuron '{Id}' cannot fire while executing command '{id}': fire from a reaction, not a command. Schedule the work instead.");
        }

        return FireCoreAsync(signal, to, correlation, cancellationToken);
    }

    private async Task<FireOutcome> FireCoreAsync(
        Signal signal, NeuronId? to, CorrelationId? correlation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(signal);
        cancellationToken.ThrowIfCancellationRequested();

        // Re-validate: a Signal deserialized from the wire may bypass Create. Keep the
        // normalized instance â€” Create fills a blank body with "{}".
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

        var delivery = SignalDelivery.Create(signal, Id, _components.Journals.OutgoingNextSequence, TimeProvider, (ReactionContext as DeliveryReaction)?.Delivery, correlation);
        _components.Journals.AppendOutgoing(delivery);
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

    // ---- the drain's wake-ups ----

    // The reminder exists only to reactivate a cold neuron; the drain decides whether there is
    // still work. A tick is also this activation's only proof that a reminder row is out there.
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

    private ILogger? Logger => ServiceProvider.GetService<ILogger<Neuron>>();

    // A one-way call to ourselves: it returns immediately and is queued behind the current turn.
    private void Wake() => GrainFactory.GetGrain<INeuronInbox>(this.GetGrainId()).Drain().Ignore();

    protected new IDisposable RegisterTimer(Func<object, Task> callback, object state, TimeSpan dueTime, TimeSpan period)
        => throw new InvalidOperationException($"{nameof(RegisterTimer)} creates interleaving callbacks, but neurons require serialized turns.");
}
