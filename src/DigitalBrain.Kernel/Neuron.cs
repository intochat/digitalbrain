using System.Diagnostics;
using DigitalBrain.Abstractions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.Serialization;

namespace DigitalBrain.Kernel;

public abstract class Neuron : DurableGrain, INeuron, IRemindable
{
    private const string IncomingJournalName = "incoming";
    private const string OutgoingJournalName = "outgoing";
    private const string OutboxName = "outbox";
    private const string HandledName = "handled";
    private const string OutboxReminderName = "db.outbox";

    private const int RememberedDeliveries = 4096;

    private static readonly TimeSpan RetryInterval = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan ReminderInterval = TimeSpan.FromMinutes(1);

    private readonly NeuronFeed _incoming;
    private readonly NeuronFeed _outgoing;
    private readonly IDurableList<byte[]> _outbox;
    private readonly IDurableList<Guid> _handled;
    private readonly HashSet<SynapseId> _remembered = [];
    private readonly List<SynapseDelivery> _firedWhileHandling = [];
    private readonly Serializer<OutboxEntry> _entries;
    private readonly Serializer<Synapse> _synapses;
    private readonly TimeProvider _clock;

    private SynapseDelivery? _handling;
    private int _handlingDepth;
    private IGrainTimer? _draining;
    private bool _wakeUpRegistered;

    protected Neuron()
    {
        _incoming = new NeuronFeed(ServiceProvider, IncomingJournalName);
        _outgoing = new NeuronFeed(ServiceProvider, OutgoingJournalName);
        _outbox = ServiceProvider.GetRequiredKeyedService<IDurableList<byte[]>>(OutboxName);
        _handled = ServiceProvider.GetRequiredKeyedService<IDurableList<Guid>>(HandledName);
        _entries = ServiceProvider.GetRequiredService<Serializer<OutboxEntry>>();
        _synapses = ServiceProvider.GetRequiredService<Serializer<Synapse>>();
        _clock = ServiceProvider.GetService<TimeProvider>() ?? TimeProvider.System;
    }

    public NeuronId Id => NeuronId.FromGrainKey(this.GetGrainId().Type.ToString()!, this.GetPrimaryKeyString());

    public sealed override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        NeuronConcurrency.RequireSerializedTurns(GetType());

        await base.OnActivateAsync(cancellationToken);

        _wakeUpRegistered = await this.GetReminder(OutboxReminderName) is not null;

        RecallHandledDeliveries();

        var registry = SubscriptionRegistry.For(GrainFactory, Id.Owner);

        foreach (var handled in SynapseWiring.HandledSynapseTypes(GetType()))
        {
            await registry.RegisterAsync(handled.FullName!, Id);
        }

        ScheduleDrain();
    }

    public async Task ReceiveReminder(string reminderName, TickStatus status)
    {
        ScheduleDrain();

        await ForgetWakeUpWhenOutboxIsEmptyAsync();
    }

    public async Task DeliverAsync(SynapseDelivery delivery)
    {
        ArgumentNullException.ThrowIfNull(delivery);

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

        var committedOutbox = _outbox.Count;
        var committedHandled = _handled.Count;

        _firedWhileHandling.Clear();

        try
        {
            await DispatchAsync(Snapshot(delivery.Synapse));

            foreach (var fired in _firedWhileHandling)
            {
                _outgoing.Append(fired);
            }

            _incoming.Append(delivery);

            Remember(delivery.SynapseId);

            await CommitAsync(CancellationToken.None);
        }
        catch
        {
            Discard(_outbox, committedOutbox);
            Discard(_handled, committedHandled);

            RecallHandledDeliveries();

            throw;
        }
        finally
        {
            _firedWhileHandling.Clear();
            _handling = null;
            _handlingDepth = 0;
        }

        ScheduleDrain();
    }

    public Task<JournalRead> ReadJournalAsync(JournalKind kind, long afterSequence)
        => Task.FromResult(FeedFor(kind).Read(afterSequence));

    private NeuronFeed FeedFor(JournalKind kind) => kind switch
    {
        JournalKind.Incoming => _incoming,
        JournalKind.Outgoing => _outgoing,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    protected Task SendAsync(NeuronId receiver, Synapse synapse)
    {
        ArgumentNullException.ThrowIfNull(synapse);

        return FireAsync(synapse, [receiver]);
    }

    protected Task ReplyAsync(Synapse synapse)
    {
        ArgumentNullException.ThrowIfNull(synapse);

        var answered = _handling
            ?? throw new InvalidOperationException($"{GetType().Name} has nothing to reply to: replies are only valid while handling a synapse.");

        return FireAsync(synapse, [answered.Caller]);
    }

    protected async Task EmitAsync(Synapse synapse)
    {
        ArgumentNullException.ThrowIfNull(synapse);

        var subscribers = await SubscriptionRegistry.For(GrainFactory, Id.Owner)
            .SubscribersAsync(synapse.GetType().FullName!);

        await FireAsync(synapse, [.. subscribers]);
    }

    protected async Task<string> AskModelAsync(ModelTier tier, string prompt, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);

        var model = ServiceProvider.GetKeyedService<IChatClient>(tier)
            ?? throw new InvalidOperationException(
                $"{GetType().Name} asked for the {tier} model tier, but no model is bound to it. Tiers are bound in AppHost configuration.");

        var answer = await model.GetResponseAsync([new ChatMessage(ChatRole.User, prompt)], options: null, cancellationToken);

        return answer.Text;
    }

    protected IGrainTimer RegisterGrainTimer(
        Func<CancellationToken, Task> callback,
        GrainTimerCreationOptions options)
    {
        NeuronConcurrency.RequireSerializedTimer(options);

        return GrainBaseExtensions.RegisterGrainTimer(this, callback, options);
    }

    protected IGrainTimer RegisterGrainTimer(
        Func<Task> callback,
        GrainTimerCreationOptions options)
    {
        NeuronConcurrency.RequireSerializedTimer(options);

        return GrainBaseExtensions.RegisterGrainTimer(this, callback, options);
    }

    protected IGrainTimer RegisterGrainTimer<TState>(
        Func<TState, CancellationToken, Task> callback,
        TState state,
        GrainTimerCreationOptions options)
    {
        NeuronConcurrency.RequireSerializedTimer(options);

        return GrainBaseExtensions.RegisterGrainTimer(this, callback, state, options);
    }

    protected IGrainTimer RegisterGrainTimer<TState>(
        Func<TState, Task> callback,
        TState state,
        GrainTimerCreationOptions options)
    {
        NeuronConcurrency.RequireSerializedTimer(options);

        return GrainBaseExtensions.RegisterGrainTimer(this, callback, state, options);
    }

    protected new IDisposable RegisterTimer(
        Func<object, Task> callback,
        object state,
        TimeSpan dueTime,
        TimeSpan period)
        => throw new InvalidOperationException(
            $"{nameof(RegisterTimer)} creates interleaving callbacks, but neurons require serialized turns.");

    internal async Task<SynapseDelivery> FireAsync(Synapse synapse, NeuronId[] receivers)
    {
        var sequence = _outgoing.NextSequence
            + (_handling is null ? 0 : _firedWhileHandling.Count);
        var delivery = SynapseDelivery.Create(Snapshot(synapse), Id, sequence, _handling, _clock);

        if (_handling is null)
        {
            _outgoing.Append(delivery);
        }
        else
        {
            _firedWhileHandling.Add(delivery);
        }

        if (receivers.Length > 0)
        {
            _outbox.Add(_entries.SerializeToArray(
                new OutboxEntry(delivery, receivers, _handlingDepth + 1, Attempts: 0)));
        }

        if (_handling is null)
        {
            await CommitAsync(CancellationToken.None);
            ScheduleDrain();
        }

        return delivery;
    }

    private async Task CommitAsync(CancellationToken cancellationToken)
    {
        if (_outbox.Count > 0 && !_wakeUpRegistered)
        {
            await this.RegisterOrUpdateReminder(OutboxReminderName, ReminderInterval, ReminderInterval);
            _wakeUpRegistered = true;
        }

        await WriteStateAsync(cancellationToken);

        await ForgetWakeUpWhenOutboxIsEmptyAsync();
    }

    private async Task ForgetWakeUpWhenOutboxIsEmptyAsync()
    {
        if (_outbox.Count > 0 || !_wakeUpRegistered)
        {
            return;
        }

        if (await this.GetReminder(OutboxReminderName) is { } registered)
        {
            await this.UnregisterReminder(registered);
        }

        _wakeUpRegistered = false;
    }

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
        while (_outbox.Count > 0)
        {
            var committed = _entries.Deserialize(_outbox[0]);
            var entry = committed with { Attempts = committed.Attempts + 1 };

            if (entry.Depth > DeliveryPolicy.MaximumDepth)
            {
                Abandon(entry, $"exceeded the maximum synapse depth of {DeliveryPolicy.MaximumDepth}");
                _outbox.RemoveAt(0);

                continue;
            }

            var undelivered = new List<NeuronId>();

            foreach (var receiver in entry.Pending)
            {
                if (!await TryDeliverAsync(entry, receiver))
                {
                    undelivered.Add(receiver);
                }
            }

            if (undelivered.Count > 0)
            {
                if (Exhausted(entry))
                {
                    Abandon(entry, $"undeliverable to {string.Join(", ", undelivered)} after {entry.Attempts} attempts");
                    _outbox.RemoveAt(0);

                    continue;
                }

                _outbox[0] = _entries.SerializeToArray(entry with { Pending = [.. undelivered] });

                break;
            }

            _outbox.RemoveAt(0);
        }

        await CommitAsync(cancellationToken);

        StopDrainingWhenOutboxIsEmpty();
    }

    private bool Exhausted(OutboxEntry entry)
        => entry.Attempts >= DeliveryPolicy.MaximumAttempts
        || _clock.GetUtcNow() - entry.Delivery.Timestamp > DeliveryPolicy.RetryHorizon;

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Any failure other than a permanent refusal keeps the receiver pending so the outbox redelivers it; letting it escape would abandon the delivery guarantee.")]
    private async Task<bool> TryDeliverAsync(OutboxEntry entry, NeuronId receiver)
    {
        DeliveryPolicy.CarryDepth(entry.Depth);

        try
        {
            if (receiver == Id)
            {
                await DeliverAsync(entry.Delivery);
            }
            else
            {
                await GrainFactory.GetGrain<INeuron>(receiver.ToGrainId()).DeliverAsync(entry.Delivery);
            }

            return true;
        }
        catch (NeuronAuthorizationException refusal)
        {
            Record("refused", entry.Delivery, receiver, refusal.Message);

            return true;
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

    private bool HasAlreadyHandled(SynapseDelivery delivery)
        => _remembered.Contains(delivery.SynapseId);

    private void Remember(SynapseId delivered)
    {
        _handled.Add(delivered.Value);
        _remembered.Add(delivered);

        while (_handled.Count > RememberedDeliveries)
        {
            _remembered.Remove(new SynapseId(_handled[0]));
            _handled.RemoveAt(0);
        }
    }

    private void RecallHandledDeliveries()
    {
        _remembered.Clear();

        foreach (var delivered in _handled)
        {
            _remembered.Add(new SynapseId(delivered));
        }
    }

    private Task DispatchAsync(Synapse synapse)
        => SynapseDispatch.HandlersFor(GetType()).TryGetValue(synapse.GetType(), out var handler)
            ? handler(this, synapse, CancellationToken.None)
            : Task.CompletedTask;

    private Synapse Snapshot(Synapse synapse)
        => _synapses.Deserialize(_synapses.SerializeToArray(synapse));
}
