using Orleans;

namespace DigitalBrain.Testing;

public sealed class Simulation
{
    private static readonly Dictionary<string, string> EmptyValues = new(StringComparer.Ordinal);

    private OwnerId _owner;
    private Exception? _refusal;

    public NeuronId Id => new(nameof(SimulationNeuron), Owner, "driver");

    public OwnerId Owner => _owner.Value is null
        ? throw new InvalidOperationException("The scenario has no owner. Start it with a \"Given a brain for owner\" step.")
        : _owner;

    public void OpenBrain(string owner) => _owner = new OwnerId(owner);

    public NeuronId NeuronNamed(string neuronType, string name) => new(neuronType, Owner, name);

    public Task SendAsync(string synapseTypeName, string neuronType, string name, IReadOnlyDictionary<string, string> values)
        => StimulateAsync(synapseTypeName, NeuronNamed(neuronType, name), values);

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "The driver records whatever the cluster refused with so a scenario can report the actual failure instead of letting an unexpected type escape unexplained.")]
    public async Task SendClaimingOwnerAsync(string synapseTypeName, string neuronType, string name, string targetOwner)
    {
        var receiver = new NeuronId(neuronType, new OwnerId(targetOwner), name);

        try
        {
            await StimulateAsync(synapseTypeName, receiver, EmptyValues);
            _refusal = null;
        }
        catch (Exception refusal)
        {
            _refusal = refusal;
        }
    }

    public async Task SendTwiceAsync(string synapseTypeName, string neuronType, string name)
    {
        var receiver = NeuronNamed(neuronType, name);
        var synapse = NeuronCatalog.Create(synapseTypeName, EmptyValues);

        await Driver().StimulateTwiceAsync(receiver, synapse);
    }

    public void ExpectRefusal<TRefusal>()
        where TRefusal : Exception
    {
        if (_refusal is not TRefusal)
        {
            throw new SimulationAssertionException(
                $"Expected the synapse to be refused with {typeof(TRefusal).Name}, but got {_refusal?.GetType().Name ?? "no refusal"}.");
        }
    }

    public async Task<IReadOnlyList<Synapse>> ReadJournalAsync(JournalKind kind, string neuronType, string name)
        => await Neuron(NeuronNamed(neuronType, name)).ReadJournalAsync(kind);

    public async Task RegisterAsync(string neuronType, string name)
        => await Neuron(NeuronNamed(neuronType, name)).ReadJournalAsync(JournalKind.Incoming);

    public Task AwaitHandledAsync(string neuronType, string name, string synapseTypeName)
        => SimulationCluster.Observed.AwaitHandledAsync(NeuronNamed(neuronType, name), synapseTypeName);

    public Task<int> SubscriberCountAsync(string synapseTypeName)
        => SimulationCluster.Grains
            .GetGrain<ISubscriptionRegistry>(Owner.Value)
            .SubscriberCountAsync(NeuronCatalog.SynapseType(synapseTypeName).FullName!);

    private Task StimulateAsync(string synapseTypeName, NeuronId receiver, IReadOnlyDictionary<string, string> values)
        => Driver().StimulateAsync(receiver, NeuronCatalog.Create(synapseTypeName, values));

    private ISimulationNeuron Driver() => SimulationCluster.Grains.GetGrain<ISimulationNeuron>(Id.ToGrainId());

    private static INeuron Neuron(NeuronId id) => SimulationCluster.Grains.GetGrain<INeuron>(id.ToGrainId());
}
