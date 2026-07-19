using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.Runtime;
using Orleans.Serialization;

namespace DigitalBrain;

public abstract class Neuron : DurableGrain, INeuron
{
    private const string IncomingJournalName = "incoming";
    private const string OutgoingJournalName = "outgoing";

    private readonly IDurableList<byte[]> _incoming;
    private readonly IDurableList<byte[]> _outgoing;
    private readonly Serializer<Synapse> _synapses;

    private SynapseMetadata? _handling;

    protected Neuron()
    {
        _incoming = ServiceProvider.GetRequiredKeyedService<IDurableList<byte[]>>(IncomingJournalName);
        _outgoing = ServiceProvider.GetRequiredKeyedService<IDurableList<byte[]>>(OutgoingJournalName);
        _synapses = ServiceProvider.GetRequiredService<Serializer<Synapse>>();
    }

    public NeuronId Id => NeuronId.FromGrainKey(this.GetGrainId().Type.ToString()!, this.GetPrimaryKeyString());

    protected IReadOnlyList<Synapse> Incoming => Read(_incoming);

    protected IReadOnlyList<Synapse> Outgoing => Read(_outgoing);

    public async Task DeliverAsync(Synapse synapse)
    {
        ArgumentNullException.ThrowIfNull(synapse);

        if (HasAlreadyHandled(synapse))
        {
            return;
        }

        _handling = synapse.Stamped;

        try
        {
            await DispatchAsync(synapse);
        }
        finally
        {
            _handling = null;
        }

        _incoming.Add(_synapses.SerializeToArray(synapse));
        await WriteStateAsync();
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

        return FireAsync(synapse, SynapseMetadata.ForSend(Id, receiver, _handling));
    }

    protected Task ReplyAsync(Synapse synapse)
    {
        ArgumentNullException.ThrowIfNull(synapse);

        var answered = _handling
            ?? throw new InvalidOperationException($"{GetType().Name} has nothing to reply to: replies are only valid while handling a synapse.");

        return FireAsync(synapse, SynapseMetadata.ForReply(Id, answered));
    }

    protected Task EmitAsync(Synapse synapse)
    {
        ArgumentNullException.ThrowIfNull(synapse);

        return FireAsync(synapse, SynapseMetadata.ForBroadcast(Id, _handling));
    }

    private async Task FireAsync(Synapse synapse, SynapseMetadata metadata)
    {
        var fired = synapse with { Metadata = metadata };

        _outgoing.Add(_synapses.SerializeToArray(fired));

        if (_handling is null)
        {
            await WriteStateAsync();
        }
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
