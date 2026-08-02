using System.Collections.Concurrent;
using DigitalBrain.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.Serialization;

namespace DigitalBrain.Kernel;

public abstract partial class Neuron :
    DurableGrain,
    INeuron,
    IOutboxDrain
{
    private const string IncomingJournalName = "incoming";
    private const string OutgoingJournalName = "outgoing";
    private const string OutboxName = "outbox";
    private const string HandledName = "handled";
    private const int RememberedDeliveries = 4096;

    private static readonly TimeSpan RetryInterval = TimeSpan.FromMilliseconds(50);
    private readonly NeuronFeed _incoming;
    private readonly NeuronFeed _outgoing;
    private readonly List<Watcher> _watchers = [];
    private readonly IDurableList<byte[]> _outbox;
    private readonly IDurableList<Guid> _handled;
    private readonly HashSet<SynapseId> _remembered = [];
    private readonly ConcurrentDictionary<Guid, SynapseDelivery> _streamedCapabilityRequests = new();
    private readonly List<SynapseDelivery> _firedWhileHandling = [];
    private readonly List<Action> _turnRollbacks = [];
    private readonly Serializer<OutboxEntry> _entries;
    private readonly Serializer<Synapse> _synapses;
    private SynapseDelivery? _handling;
    private int _handlingDepth;
    private CancellationToken _turnCancellation;
    private TurnCheckpoint? _turnCheckpoint;
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
        TimeProvider =
            ServiceProvider.GetKeyedService<TimeProvider>(NeuronTime.ServiceKey)
            ?? System.TimeProvider.System;
    }

    public NeuronId Id => NeuronId.FromGrainKey(this.GetGrainId().Type.ToString()!, this.GetPrimaryKeyString());

    protected TimeProvider TimeProvider { get; }

    protected NeuronId? CurrentDeliveryCaller => _handling?.Caller;

    // Orleans request / turn cancellation captured at Deliver entry for handlers and grain re-entry.
    protected CancellationToken TurnCancellationToken => _turnCancellation;

}
