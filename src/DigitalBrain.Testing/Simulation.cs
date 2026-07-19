using DigitalBrain.Abstractions;
using DigitalBrain.Client;

namespace DigitalBrain.Testing;

public sealed class Simulation
{
    private const int SettleProbes = 200;
    private const int SettleProbesWithoutChange = 5;

    private static readonly TimeSpan SettleProbeInterval = TimeSpan.FromMilliseconds(50);

    private static readonly Dictionary<string, string> EmptyValues = new(StringComparer.Ordinal);

    private readonly List<NeuronId> _registered = [];

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

    public BrainClient Client => new(SimulationCluster.Grains, Owner);

    public Task ClientFireAsync(string synapseTypeName, string neuronType, string name)
        => Client.FireAsync(neuronType, name, NeuronCatalog.Create(synapseTypeName, EmptyValues));

    public Task ClientFireExpectingRefusalAsync(string synapseTypeName, string neuronType, string name, string targetOwner)
        => CaptureRefusalAsync(() => Client.FireAsync(
            new NeuronId(neuronType, new OwnerId(targetOwner), name),
            NeuronCatalog.Create(synapseTypeName, EmptyValues)));

    public Task<JournalRead> ClientReadJournalAsync(JournalKind kind, string neuronType, string name, long afterSequence)
        => Client.Neuron(neuronType, name).ReadJournalAsync(kind, afterSequence);

    public Task SendExpectingRefusalAsync(string synapseTypeName, string neuronType, string name)
        => CaptureRefusalAsync(() => StimulateAsync(synapseTypeName, NeuronNamed(neuronType, name), EmptyValues));

    public Task SendClaimingOwnerAsync(string synapseTypeName, string neuronType, string name, string targetOwner)
        => CaptureRefusalAsync(
            () => StimulateAsync(synapseTypeName, new NeuronId(neuronType, new OwnerId(targetOwner), name), EmptyValues));

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "A step that declares the cluster will refuse records whatever it threw so the assertion can report the actual failure instead of letting an unexpected type escape unexplained.")]
    private async Task CaptureRefusalAsync(Func<Task> stimulus)
    {
        try
        {
            await stimulus();

            throw new SimulationAssertionException("The scenario expected a refusal, but the synapse was accepted.");
        }
        catch (SimulationAssertionException)
        {
            throw;
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

    public async Task<JournalRead> ReadJournalAsync(JournalKind kind, string neuronType, string name, long afterSequence)
        => await Neuron(NeuronNamed(neuronType, name)).ReadJournalAsync(kind, afterSequence);

    public static async Task<JournalRead> ReadJournalOfOwnerAsync(
        JournalKind kind,
        string owner,
        string neuronType,
        string name,
        long afterSequence)
        => await Neuron(new NeuronId(neuronType, new OwnerId(owner), name)).ReadJournalAsync(kind, afterSequence);

    public async Task RegisterAsync(string neuronType, string name)
        => _ = await Neuron(NeuronNamed(neuronType, name)).ReadJournalAsync(JournalKind.Incoming, afterSequence: 0);

    public Task AwaitHandledAsync(string neuronType, string name, string synapseTypeName)
        => SimulationCluster.Observed.AwaitHandledAsync(NeuronNamed(neuronType, name), synapseTypeName);

    public async Task<int> SettleAsync(JournalKind kind, string neuronType, string name)
    {
        var neuron = Neuron(NeuronNamed(neuronType, name));
        long previousSequence = -1;
        var unchanged = 0;
        var retained = 0;

        for (var probe = 0; probe < SettleProbes; probe++)
        {
            await Task.Delay(SettleProbeInterval);

            var current = await neuron.ReadJournalAsync(kind, afterSequence: 0);
            retained = current.ResetSnapshot?.RetainedCount ?? current.Delta.Count;

            unchanged = current.ResumeSequence == previousSequence ? unchanged + 1 : 0;

            if (unchanged >= SettleProbesWithoutChange)
            {
                return retained;
            }

            previousSequence = current.ResumeSequence;
        }

        throw new SimulationAssertionException(
            $"The {kind} journal of {neuronType} '{name}' never stopped changing: it reached sequence {previousSequence} with {retained} retained entries after {SettleProbes} probes.");
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

    public Task SubscribeInOwnerExpectingRefusalAsync(string neuronType, string name, string synapseTypeName, string registryOwner)
        => CaptureRefusalAsync(() => SimulationCluster.Grains
            .GetGrain<ISubscriptionRegistry>(registryOwner)
            .RegisterAsync(NeuronCatalog.SynapseType(synapseTypeName).FullName!, NeuronNamed(neuronType, name)));

    public Task<int> SubscriberCountAsync(string synapseTypeName)
        => SimulationCluster.Grains
            .GetGrain<ISubscriptionRegistry>(Owner.Value)
            .SubscriberCountAsync(NeuronCatalog.SynapseType(synapseTypeName).FullName!);

    private Task StimulateAsync(string synapseTypeName, NeuronId receiver, IReadOnlyDictionary<string, string> values)
        => Driver().StimulateAsync(receiver, NeuronCatalog.Create(synapseTypeName, values));

    private ISimulationNeuron Driver() => SimulationCluster.Grains.GetGrain<ISimulationNeuron>(Id.ToGrainId());

    private static INeuron Neuron(NeuronId id) => SimulationCluster.Grains.GetGrain<INeuron>(id.ToGrainId());
}
