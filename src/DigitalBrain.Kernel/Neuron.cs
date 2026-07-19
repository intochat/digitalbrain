using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Orleans;
using Orleans.Journaling;
using Orleans.Runtime;
using Orleans.Serialization;

namespace DigitalBrain;

public abstract class Neuron : DurableGrain, INeuron, IRemindable
{
    private const string IncomingJournalName = "incoming";
    private const string OutgoingJournalName = "outgoing";
    private const string OutboxName = "outbox";
    private const string OutboxReminderName = "db.outbox";

    private static readonly TimeSpan RetryInterval = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan ReminderInterval = TimeSpan.FromMinutes(1);

    private readonly IDurableList<byte[]> _incoming;
    private readonly IDurableList<byte[]> _outgoing;
    private readonly IDurableList<byte[]> _outbox;
    private readonly Serializer<Synapse> _synapses;
    private readonly Serializer<OutboxEntry> _entries;
    private readonly TimeProvider _clock;

    private SynapseMetadata? _handling;
    private int _handlingDepth;
    private IGrainTimer? _draining;
    private bool _wakeUpRegistered;

    protected Neuron()
    {
        _incoming = ServiceProvider.GetRequiredKeyedService<IDurableList<byte[]>>(IncomingJournalName);
        _outgoing = ServiceProvider.GetRequiredKeyedService<IDurableList<byte[]>>(OutgoingJournalName);
        _outbox = ServiceProvider.GetRequiredKeyedService<IDurableList<byte[]>>(OutboxName);
        _synapses = ServiceProvider.GetRequiredService<Serializer<Synapse>>();
        _entries = ServiceProvider.GetRequiredService<Serializer<OutboxEntry>>();
        _clock = ServiceProvider.GetService<TimeProvider>() ?? TimeProvider.System;
    }

    public NeuronId Id => NeuronId.FromGrainKey(this.GetGrainId().Type.ToString()!, this.GetPrimaryKeyString());

    protected IReadOnlyList<Synapse> Incoming => Read(_incoming);

    protected IReadOnlyList<Synapse> Outgoing => Read(_outgoing);

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await base.OnActivateAsync(cancellationToken);

        var registry = SubscriptionRegistry.For(GrainFactory, Id.Owner);

        foreach (var handled in SynapseWiring.HandledSynapseTypes(GetType()))
        {
            await registry.RegisterAsync(handled.FullName!, Id);
        }

        ScheduleDrain();
    }

    public Task ReceiveReminder(string reminderName, TickStatus status)
    {
        ScheduleDrain();

        return Task.CompletedTask;
    }

    public async Task DeliverAsync(Synapse synapse)
    {
        ArgumentNullException.ThrowIfNull(synapse);

        if (HasAlreadyHandled(synapse))
        {
            return;
        }

        using var handling = SynapseTelemetry.Source.StartActivity("handle");

        handling?.SetTag(SynapseTelemetry.ReceiverTag, Id.ToString());
        handling?.SetTag(SynapseTelemetry.SynapseTag, synapse.GetType().Name);
        handling?.SetTag(SynapseTelemetry.CorrelationTag, synapse.Stamped.CorrelationId.ToString());

        _handling = synapse.Stamped;
        _handlingDepth = SynapseDelivery.InboundDepth();

        var committedOutgoing = _outgoing.Count;
        var committedOutbox = _outbox.Count;

        try
        {
            await DispatchAsync(synapse);
        }
        catch
        {
            Discard(_outgoing, committedOutgoing);
            Discard(_outbox, committedOutbox);

            throw;
        }
        finally
        {
            _handling = null;
            _handlingDepth = 0;
        }

        _incoming.Add(_synapses.SerializeToArray(synapse));
        await CommitAsync(CancellationToken.None);

        ScheduleDrain();
    }

    public Task<IReadOnlyList<Synapse>> ReadJournalAsync(JournalKind kind) => Task.FromResult(kind switch
    {
        JournalKind.Incoming => Incoming,
        JournalKind.Outgoing => Outgoing,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    });

    protected Task SendAsync(NeuronId receiver, Synapse synapse)
    {
        ArgumentNullException.ThrowIfNull(synapse);

        return FireAsync(synapse, SynapseMetadata.ForSend(Id, receiver, _handling), [receiver]);
    }

    protected Task ReplyAsync(Synapse synapse)
    {
        ArgumentNullException.ThrowIfNull(synapse);

        var answered = _handling
            ?? throw new InvalidOperationException($"{GetType().Name} has nothing to reply to: replies are only valid while handling a synapse.");

        var metadata = SynapseMetadata.ForReply(Id, answered);

        return FireAsync(synapse, metadata, [metadata.Receiver!.Value]);
    }

    protected async Task EmitAsync(Synapse synapse)
    {
        ArgumentNullException.ThrowIfNull(synapse);

        var subscribers = await SubscriptionRegistry.For(GrainFactory, Id.Owner)
            .SubscribersAsync(synapse.GetType().FullName!);

        await FireAsync(synapse, SynapseMetadata.ForBroadcast(Id, _handling), [.. subscribers]);
    }

    private async Task FireAsync(Synapse synapse, SynapseMetadata metadata, NeuronId[] receivers)
    {
        var fired = synapse with { Metadata = metadata };

        _outgoing.Add(_synapses.SerializeToArray(fired));

        if (receivers.Length > 0)
        {
            _outbox.Add(_entries.SerializeToArray(
                new OutboxEntry(fired, receivers, _handlingDepth + 1, Attempts: 0, _clock.GetUtcNow())));
        }

        if (_handling is null)
        {
            await CommitAsync(CancellationToken.None);
            ScheduleDrain();
        }
    }

    private async Task CommitAsync(CancellationToken cancellationToken)
    {
        await WriteStateAsync(cancellationToken);

        var pending = _outbox.Count > 0;

        if (pending == _wakeUpRegistered)
        {
            return;
        }

        if (pending)
        {
            await this.RegisterOrUpdateReminder(OutboxReminderName, ReminderInterval, ReminderInterval);
            _wakeUpRegistered = true;
        }
        else if (await this.GetReminder(OutboxReminderName) is { } registered)
        {
            await this.UnregisterReminder(registered);
            _wakeUpRegistered = false;
        }
        else
        {
            _wakeUpRegistered = false;
        }
    }

    private static void Discard(IDurableList<byte[]> journal, int committed)
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

            if (entry.Depth > SynapseDelivery.MaximumDepth)
            {
                Abandon(entry, $"exceeded the maximum synapse depth of {SynapseDelivery.MaximumDepth}");
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
        => entry.Attempts >= SynapseDelivery.MaximumAttempts
        || _clock.GetUtcNow() - entry.FirstAttempted > SynapseDelivery.RetryHorizon;

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Any failure other than a permanent refusal keeps the receiver pending so the outbox redelivers it; letting it escape would abandon the delivery guarantee.")]
    private async Task<bool> TryDeliverAsync(OutboxEntry entry, NeuronId receiver)
    {
        SynapseDelivery.CarryDepth(entry.Depth);

        try
        {
            if (receiver == Id)
            {
                await DeliverAsync(entry.Synapse);
            }
            else
            {
                await GrainFactory.GetGrain<INeuron>(receiver.ToGrainId()).DeliverAsync(entry.Synapse);
            }

            return true;
        }
        catch (NeuronAuthorizationException refusal)
        {
            Record("refused", entry.Synapse, receiver, refusal.Message);

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
            Record("abandoned", entry.Synapse, receiver, reason);
        }
    }

    private static void Record(string outcome, Synapse synapse, NeuronId receiver, string reason)
    {
        using var recorded = SynapseTelemetry.Source.StartActivity(outcome);

        recorded?.SetTag(SynapseTelemetry.ReceiverTag, receiver.ToString());
        recorded?.SetTag(SynapseTelemetry.SynapseTag, synapse.GetType().Name);
        recorded?.SetTag(SynapseTelemetry.CorrelationTag, synapse.Stamped.CorrelationId.ToString());
        recorded?.SetStatus(ActivityStatusCode.Error, reason);
    }

    private List<Synapse> Read(IDurableList<byte[]> journal)
        => journal.Select(_synapses.Deserialize).ToList();

    private bool HasAlreadyHandled(Synapse synapse)
        => Incoming.Any(recorded => recorded.Stamped.SynapseId == synapse.Stamped.SynapseId);

    private Task DispatchAsync(Synapse synapse)
        => SynapseDispatch.HandlersFor(GetType()).TryGetValue(synapse.GetType(), out var handler)
            ? handler(this, synapse, CancellationToken.None)
            : Task.CompletedTask;
}
