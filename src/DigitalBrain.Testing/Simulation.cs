using Orleans;

namespace DigitalBrain.Testing;

public sealed class Simulation
{
    private const string SimulationNeuronType = "Simulation";

    private OwnerId _owner;

    public NeuronId Id => new(SimulationNeuronType, Owner, "driver");

    public OwnerId Owner => _owner.Value is null
        ? throw new InvalidOperationException("The scenario has no owner. Start it with a \"Given a brain for owner\" step.")
        : _owner;

    public void OpenBrain(string owner) => _owner = new OwnerId(owner);

    public NeuronId NeuronNamed(string neuronType, string name) => new(neuronType, Owner, name);

    public async Task SendAsync(string synapseTypeName, string neuronType, string name, IReadOnlyDictionary<string, string> values)
    {
        var receiver = NeuronNamed(neuronType, name);
        var synapse = NeuronCatalog.Create(synapseTypeName, values) with
        {
            Metadata = SynapseMetadata.ForSend(Id, receiver),
        };

        await Neuron(receiver).DeliverAsync(synapse);
    }

    public async Task<IReadOnlyList<Synapse>> ReadJournalAsync(JournalKind kind, string neuronType, string name)
        => await Neuron(NeuronNamed(neuronType, name)).ReadJournalAsync(kind);

    private static INeuron Neuron(NeuronId id)
        => SimulationCluster.Grains.GetGrain<INeuron>(id.GrainKey, grainClassNamePrefix: null);
}
