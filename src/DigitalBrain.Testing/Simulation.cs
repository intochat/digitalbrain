using Orleans;
using Orleans.Runtime;

namespace DigitalBrain.Testing;

public sealed class Simulation
{
    private const int SettleProbes = 200;

    private static readonly Dictionary<string, string> EmptyValues = new(StringComparer.Ordinal);

    private readonly List<NeuronId> _registered = [];

    private OwnerId _owner;
    private Exception? _refusal;
    private bool _refusalAsserted;

    public NeuronId Id => new(nameof(SimulationNeuron), Owner, "driver");

    public OwnerId Owner => _owner.Value is null
        ? throw new InvalidOperationException("The scenario has no owner. Start it with a \"Given a brain for owner\" step.")
        : _owner;

    public void OpenBrain(string owner) => _owner = new OwnerId(owner);

    public NeuronId NeuronNamed(string neuronType, string name) => new(neuronType, Owner, name);

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "The driver records whatever the cluster refused with so a scenario can report the actual failure; an unasserted refusal is rethrown when the scenario ends.")]
    public async Task SendAsync(string synapseTypeName, string neuronType, string name, IReadOnlyDictionary<string, string> values)
    {
        try
        {
            await StimulateAsync(synapseTypeName, NeuronNamed(neuronType, name), values);
            _refusal = null;
        }
        catch (Exception refusal)
        {
            _refusal = refusal;
        }
    }

    public void RethrowUnassertedRefusal()
    {
        if (_refusal is { } unasserted && !_refusalAsserted)
        {
            throw new SimulationAssertionException(
                $"The scenario left a refusal unasserted: {unasserted.GetType().Name}: {unasserted.Message}", unasserted);
        }
    }

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
        _refusalAsserted = true;

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

    public async Task<int> SettleAsync(JournalKind kind, string neuronType, string name)
    {
        var neuron = Neuron(NeuronNamed(neuronType, name));
        var previous = -1;

        for (var probe = 0; probe < SettleProbes; probe++)
        {
            var current = (await neuron.ReadJournalAsync(kind)).Count;

            if (current == previous)
            {
                return current;
            }

            previous = current;
        }

        return previous;
    }

    public async Task RegisterManyAsync(int count, string neuronType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(neuronType);

        _registered.Clear();

        for (var index = 0; index < count; index++)
        {
            var name = $"instance-{index}";

            await RegisterAsync(neuronType, name);
            _registered.Add(NeuronNamed(neuronType, name));
        }
    }

    public static async Task<int> HostingSiloCountAsync(params NeuronId[] neurons)
    {
        var management = SimulationCluster.Grains.GetGrain<IManagementGrain>(0);
        var hosts = await management.GetDetailedGrainStatistics();

        return hosts
            .Where(statistic => neurons.Any(neuron => statistic.GrainId == neuron.ToGrainId()))
            .Select(statistic => statistic.SiloAddress)
            .Distinct()
            .Count();
    }

    public async Task AwaitAllRegisteredHandledAsync(string synapseTypeName)
    {
        foreach (var neuron in _registered)
        {
            await SimulationCluster.Observed.AwaitHandledAsync(neuron, synapseTypeName);
        }
    }

    public Task<int> SubscriberCountAsync(string synapseTypeName)
        => SimulationCluster.Grains
            .GetGrain<ISubscriptionRegistry>(Owner.Value)
            .SubscriberCountAsync(NeuronCatalog.SynapseType(synapseTypeName).FullName!);

    private Task StimulateAsync(string synapseTypeName, NeuronId receiver, IReadOnlyDictionary<string, string> values)
        => Driver().StimulateAsync(receiver, NeuronCatalog.Create(synapseTypeName, values));

    private ISimulationNeuron Driver() => SimulationCluster.Grains.GetGrain<ISimulationNeuron>(Id.ToGrainId());

    private static INeuron Neuron(NeuronId id) => SimulationCluster.Grains.GetGrain<INeuron>(id.ToGrainId());
}
