using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.Runtime;
using Orleans.Serialization;

namespace DigitalBrain;

public abstract class Neuron : DurableGrain, INeuron
{
    private const string IncomingJournalName = "incoming";
    private const string OutgoingJournalName = "outgoing";
    private const string OutboxName = "outbox";

    private readonly IDurableList<byte[]> _incoming;
    private readonly IDurableList<byte[]> _outgoing;
    private readonly IDurableList<byte[]> _outbox;
    private readonly Serializer<Synapse> _synapses;
    private readonly Serializer<OutboxEntry> _entries;

    private static readonly TimeSpan RetryInterval = TimeSpan.FromMilliseconds(50);

    private SynapseMetadata? _handling;
    private IGrainTimer? _draining;

    protected Neuron()
    {
        _incoming = ServiceProvider.GetRequiredKeyedService<IDurableList<byte[]>>(IncomingJournalName);
        _outgoing = ServiceProvider.GetRequiredKeyedService<IDurableList<byte[]>>(OutgoingJournalName);
        _outbox = ServiceProvider.GetRequiredKeyedService<IDurableList<byte[]>>(OutboxName);
        _synapses = ServiceProvider.GetRequiredService<Serializer<Synapse>>();
        _entries = ServiceProvider.GetRequiredService<Serializer<OutboxEntry>>();
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
        }

        _incoming.Add(_synapses.SerializeToArray(synapse));
        await WriteStateAsync();

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
            _outbox.Add(_entries.SerializeToArray(new OutboxEntry(fired, receivers)));
        }

        if (_handling is null)
        {
            await WriteStateAsync();
            ScheduleDrain();
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
            var entry = _entries.Deserialize(_outbox[0]);
            var undelivered = new List<NeuronId>();

            foreach (var receiver in entry.Pending)
            {
                if (!await TryDeliverAsync(entry.Synapse, receiver))
                {
                    undelivered.Add(receiver);
                }
            }

            if (undelivered.Count > 0)
            {
                _outbox[0] = _entries.SerializeToArray(entry with { Pending = [.. undelivered] });
                break;
            }

            _outbox.RemoveAt(0);
        }

        await WriteStateAsync(cancellationToken);

        StopDrainingWhenOutboxIsEmpty();
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Any failure other than a permanent refusal keeps the receiver pending so the outbox redelivers it; letting it escape would abandon the delivery guarantee.")]
    private async Task<bool> TryDeliverAsync(Synapse synapse, NeuronId receiver)
    {
        try
        {
            if (receiver == Id)
            {
                await DeliverAsync(synapse);
            }
            else
            {
                await GrainFactory.GetGrain<INeuron>(receiver.ToGrainId()).DeliverAsync(synapse);
            }

            return true;
        }
        catch (NeuronAuthorizationException refusal)
        {
            RecordRefusal(synapse, receiver, refusal);

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static void RecordRefusal(Synapse synapse, NeuronId receiver, Exception refusal)
    {
        using var refused = SynapseTelemetry.Source.StartActivity("refused");

        refused?.SetTag(SynapseTelemetry.ReceiverTag, receiver.ToString());
        refused?.SetTag(SynapseTelemetry.SynapseTag, synapse.GetType().Name);
        refused?.SetTag(SynapseTelemetry.CorrelationTag, synapse.Stamped.CorrelationId.ToString());
        refused?.SetStatus(ActivityStatusCode.Error, refusal.Message);
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
