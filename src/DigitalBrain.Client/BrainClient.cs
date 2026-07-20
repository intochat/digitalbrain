using DigitalBrain.Abstractions;

namespace DigitalBrain.Client;

[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design",
    "CA1030:Use events where appropriate",
    Justification = "Fire is the contract's ratified verb for sending a synapse into the brain; it raises no event.")]
public sealed class BrainClient(IGrainFactory grains, OwnerId owner)
{
    private const string SessionName = "session";

    public OwnerId Owner { get; } = owner;

    public NeuronHandle Neuron(string neuronType, string name) => new(SessionNeuron(), new NeuronId(neuronType, Owner, name));

    public NeuronHandle Neuron<TNeuron>(string name)
        where TNeuron : INeuron
        => new(SessionNeuron(), NeuronId.For<TNeuron>(Owner, name));

    public NeuronHandle Session => new(SessionNeuron(), SessionId);

    public Task FireAsync(NeuronId receiver, Synapse synapse) => SessionNeuron().FireAsync(receiver, synapse);

    public Task FireAsync(string neuronType, string name, Synapse synapse)
        => FireAsync(new NeuronId(neuronType, Owner, name), synapse);

    private NeuronId SessionId => new(ISessionNeuron.GrainTypeName, Owner, SessionName);

    private ISessionNeuron SessionNeuron() => grains.GetGrain<ISessionNeuron>(SessionId.ToGrainId());
}

public sealed class NeuronHandle
{
    private readonly ISessionNeuron _session;

    internal NeuronHandle(ISessionNeuron session, NeuronId id)
    {
        _session = session;
        Id = id;
    }

    public NeuronId Id { get; }

    public Task<JournalRead> ReadJournalAsync(JournalKind kind, long afterSequence)
        => _session.ReadNeuronJournalAsync(Id, kind, afterSequence);
}
