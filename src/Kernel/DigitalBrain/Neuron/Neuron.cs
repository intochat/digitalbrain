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

// A durable actor with one receive slot. Owns its synapses, two bounded journals, and the
// latest signal of each type it received. Fire travels along synapses; nothing else routes.
public abstract class Neuron : DurableGrain, INeuron, INeuronInbox
{
    // Latest-per-type is keyed by type name, so a caller putting identity in the type would
    // grow it without bound. The cap turns that mistake into one sentence of advice.
    public const int MaxSignalTypesPerNeuron = 256;

    private readonly NeuronActivationComponents _components;

    private readonly CancellationTokenSource _activation = new();
    private readonly RetryTimer _retry;
    private SignalDelivery? _handling;
    private (SignalId Id, CancellationTokenSource Cancellation)? _reacting;

    protected Neuron(NeuronRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        _components = runtime.Bind(ServiceProvider, Id);
        _retry = new RetryTimer(this, _ => ((INeuronInbox)this).Drain());
    }

    public NeuronId Id => NeuronId.FromGrainId(this.GetGrainId());

    protected TimeProvider TimeProvider => _components.Clock;

    // A durable write belongs to the activation, so request cancellation cannot interrupt it.
    protected Task PersistAsync() => WriteStateAsync(_activation.Token).AsTask();

    // The delivery this turn is reacting to, or null outside ReceiveAsync.
    protected SignalDelivery? CurrentDelivery => _handling;

    public sealed override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        NeuronConcurrency.RequireSerializedTurns(GetType());
        await base.OnActivateAsync(cancellationToken).ConfigureAwait(true);
        await OnNeuronActivatedAsync(cancellationToken).ConfigureAwait(true);

        // Entries accepted before the last deactivation are still pending: resume the drain.
        if (_components.Pending.Peek() is not null)
        {
            Wake();
            _retry.Arm();
        }
    }

    // Shutting down cancels the reaction in flight. The pending head stays and
    // the next activation retries the entry.
    public sealed override async Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
    {
        _retry.Disarm();
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
        => FireAsync(signal, to, correlation, cancellationToken);

    public async Task Connect(NeuronId target, string signalType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(signalType);
        if (_components.Synapses.Connect(target, signalType))
        {
            await PersistAsync().ConfigureAwait(true);
        }
    }

    public async Task Disconnect(NeuronId target, string signalType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(signalType);
        if (_components.Synapses.Disconnect(target, signalType))
        {
            await PersistAsync().ConfigureAwait(true);
        }
    }

    public async Task<DeliveryAdmission> Deliver(SignalDelivery delivery, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        cancellationToken.ThrowIfCancellationRequested();

        RequireSignalTypeCapacity(delivery.Signal.Type);
        var admission = _components.Pending.TryAdmit(delivery);
        if (admission != DeliveryAdmission.Accepted)
        {
            return admission;
        }

        _components.Journals.AppendIncoming(delivery);
        _components.Latest[delivery.Signal.Type] = delivery;
        await PersistAsync().ConfigureAwait(true);

        Wake();
        return DeliveryAdmission.Accepted;
    }

    protected SignalId Schedule(Signal signal, CorrelationId? correlation = null)
    {
        ArgumentNullException.ThrowIfNull(signal);
        signal = Signal.Create(signal.Type, signal.Body);
        RequireSignalTypeCapacity(signal.Type);
        var delivery = SignalDelivery.Create(signal, Id, _components.Journals.IncomingNextSequence, TimeProvider, _handling, correlation);
        if (_components.Pending.TryAdmit(delivery) != DeliveryAdmission.Accepted)
        {
            throw new NeuronBusyException($"Neuron '{Id}' already holds {PendingWork.MaxPending} pending signals. Retry after pending work finishes.");
        }

        _components.Journals.AppendIncoming(delivery);
        _components.Latest[signal.Type] = delivery;
        Wake();
        return delivery.SignalId;
    }

    public async Task CancelReaction(SignalId pending)
    {
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
        RequestContext.Clear();
        var delivery = _components.Pending.Peek();
        if (delivery is null)
        {
            _retry.Disarm();
            return;
        }

        if (_components.Pending.IsCancelled(delivery.SignalId))
        {
            _components.Pending.Complete(delivery.SignalId);
            await PersistAsync().ConfigureAwait(true);
            Wake();
            return;
        }

        var previous = _handling;
        var previousReacting = _reacting;
        using var reaction = CancellationTokenSource.CreateLinkedTokenSource(_activation.Token);
        _handling = delivery;
        _reacting = (delivery.SignalId, reaction);
        try
        {
            try
            {
                await ReceiveAsync(delivery, reaction.Token).ConfigureAwait(true);
            }
            catch (Exception failure)
            {
                DrainTelemetry.Failed(Logger, Id, delivery.SignalId, failure);
                if (_components.Pending.IsCancelled(delivery.SignalId))
                {
                    _components.Pending.Complete(delivery.SignalId);
                    await PersistAsync().ConfigureAwait(true);
                    Wake();
                }
                else
                {
                    _retry.Arm();
                }

                return;
            }

            _components.Pending.Complete(delivery.SignalId);
            await PersistAsync().ConfigureAwait(true);
            _retry.Disarm();
            Wake();
        }
        finally
        {
            _handling = previous;
            _reacting = previousReacting;
        }
    }

    // ---- INeuron: the reads ----

    public Task<IReadOnlyList<SignalDelivery>> ReadState()
        => Task.FromResult<IReadOnlyList<SignalDelivery>>(
            [.. _components.Latest.Values.OrderBy(d => d.Signal.Type, StringComparer.Ordinal)]);

    public Task<int> ReadPendingCount() => Task.FromResult(_components.Pending.Count);

    public Task<IReadOnlyList<Synapse>> ReadSynapses() => Task.FromResult(_components.Synapses.All());

    public Task<JournalRead> ReadJournal(JournalKind kind, long afterSequence)
        => Task.FromResult(_components.Journals.Read(kind, afterSequence));

    // ---- for subclasses ----

    protected async Task<FireOutcome> FireAsync(
        Signal signal,
        NeuronId? to = null,
        CorrelationId? correlation = null,
        CancellationToken cancellationToken = default)
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

        var delivery = SignalDelivery.Create(signal, Id, _components.Journals.OutgoingNextSequence, TimeProvider, _handling, correlation);
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

    private ILogger? Logger => ServiceProvider.GetService<ILogger<Neuron>>();

    // A one-way call to ourselves: it returns immediately and is queued behind the current turn.
    private void Wake() => GrainFactory.GetGrain<INeuronInbox>(this.GetGrainId()).Drain().Ignore();

    protected new IDisposable RegisterTimer(Func<object, Task> callback, object state, TimeSpan dueTime, TimeSpan period)
        => throw new InvalidOperationException($"{nameof(RegisterTimer)} creates interleaving callbacks, but neurons require serialized turns.");
}
