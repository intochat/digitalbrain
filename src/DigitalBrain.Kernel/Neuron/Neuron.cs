using DigitalBrain.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.Serialization;

namespace DigitalBrain.Kernel;

public abstract partial class Neuron :
    DurableGrain,
    INeuron,
    IOutboxDrain,
    ICapabilityDelegationAuthority
{
    private const string IncomingJournalName = "incoming";
    private const string OutgoingJournalName = "outgoing";
    private const string OutboxName = "outbox";
    private const string HandledName = "handled";
    private const string DelegationsName = "delegations";
    private const string DelegationConsumedName = "delegation-consumed";
    private const string DelegationTerminalsName = "delegation-terminals";
    private const int RememberedDeliveries = 4096;
    private const int MaximumRememberedDelegations = 32;
    private const int ProtectedConsumedDelegations = 1;
    private const int ProtectedTerminalDelegations = 1;

    private static readonly TimeSpan RetryInterval = TimeSpan.FromMilliseconds(50);
    private readonly NeuronFeed _incoming;
    private readonly NeuronFeed _outgoing;
    private readonly List<Watcher> _watchers = [];
    private readonly IDurableList<byte[]> _outbox;
    private readonly IDurableList<Guid> _handled;
    private readonly IDurableDictionary<Guid, byte[]> _delegations;
    private readonly IDurableList<Guid> _delegationConsumed;
    private readonly IDurableList<Guid> _delegationTerminals;
    private readonly HashSet<SynapseId> _remembered = [];
    private readonly List<SynapseDelivery> _firedWhileHandling = [];
    private readonly List<Action> _turnRollbacks = [];
    private readonly Serializer<OutboxEntry> _entries;
    private readonly Serializer<Synapse> _synapses;
    private readonly Serializer<CapabilityDelegationState> _delegationStates;
    private SynapseDelivery? _handling;
    private int _handlingDepth;
    private TurnCheckpoint? _turnCheckpoint;
    private IGrainTimer? _draining;
    private bool _wakeUpRegistered;

    protected Neuron()
    {
        _incoming = new NeuronFeed(ServiceProvider, IncomingJournalName);
        _outgoing = new NeuronFeed(ServiceProvider, OutgoingJournalName);
        _outbox = ServiceProvider.GetRequiredKeyedService<IDurableList<byte[]>>(OutboxName);
        _handled = ServiceProvider.GetRequiredKeyedService<IDurableList<Guid>>(HandledName);
        _delegations = ServiceProvider.GetRequiredKeyedService<IDurableDictionary<Guid, byte[]>>(DelegationsName);
        _delegationConsumed = ServiceProvider.GetRequiredKeyedService<IDurableList<Guid>>(DelegationConsumedName);
        _delegationTerminals = ServiceProvider.GetRequiredKeyedService<IDurableList<Guid>>(DelegationTerminalsName);
        _entries = ServiceProvider.GetRequiredService<Serializer<OutboxEntry>>();
        _synapses = ServiceProvider.GetRequiredService<Serializer<Synapse>>();
        _delegationStates = ServiceProvider.GetRequiredService<Serializer<CapabilityDelegationState>>();
        TimeProvider =
            ServiceProvider.GetKeyedService<TimeProvider>(NeuronTime.ServiceKey)
            ?? System.TimeProvider.System;
    }

    public NeuronId Id => NeuronId.FromGrainKey(this.GetGrainId().Type.ToString()!, this.GetPrimaryKeyString());

    protected TimeProvider TimeProvider { get; }

}
