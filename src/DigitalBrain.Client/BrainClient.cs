using Orleans;

namespace DigitalBrain;

[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design",
    "CA1030:Use events where appropriate",
    Justification = "Fire is the contract's ratified verb for sending a synapse into the brain; it raises no event.")]
public sealed class BrainClient(IGrainFactory grains, OwnerId owner)
{
    private const string SessionName = "session";

    public OwnerId Owner { get; } = owner;

    public NeuronHandle Neuron(string neuronType, string name) => new(grains, new NeuronId(neuronType, Owner, name));

    public NeuronHandle Neuron<TNeuron>(string name)
        where TNeuron : INeuron
        => new(grains, NeuronId.For<TNeuron>(Owner, name));

    public NeuronHandle Session => new(grains, SessionId);

    public Task FireAsync(NeuronId receiver, Synapse synapse) => SessionNeuron().FireAsync(receiver, synapse);

    public Task FireAsync(string neuronType, string name, Synapse synapse)
        => FireAsync(new NeuronId(neuronType, Owner, name), synapse);

    private NeuronId SessionId => new(ISessionNeuron.GrainTypeName, Owner, SessionName);

    private ISessionNeuron SessionNeuron() => grains.GetGrain<ISessionNeuron>(SessionId.ToGrainId());
}

public sealed class NeuronHandle(IGrainFactory grains, NeuronId id)
{
    public NeuronId Id { get; } = id;

    public Task<IReadOnlyList<Synapse>> ReadJournalAsync(JournalKind kind)
        => grains.GetGrain<INeuron>(Id.ToGrainId()).ReadJournalAsync(kind);
}
