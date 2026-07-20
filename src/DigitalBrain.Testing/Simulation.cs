using DigitalBrain.Abstractions;
using DigitalBrain.Client;

namespace DigitalBrain.Testing;

public sealed class Simulation
{
    private static readonly TimeSpan SettleQuietPeriod = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan SettleLimit = TimeSpan.FromSeconds(20);

    private static readonly Dictionary<string, string> EmptyValues = new(StringComparer.Ordinal);

    private readonly List<NeuronId> _registered = [];

    private OwnerId _owner;
    private Exception? _refusal;
    private string? _lastTargetType;
    private string? _lastTargetName;

    public NeuronId Id => new(nameof(SimulationNeuron), Owner, "driver");

    public OwnerId Owner => _owner.Value is null
        ? throw new InvalidOperationException("The scenario has no owner. Start it with a \"Given a brain for owner\" step.")
        : _owner;

    public void OpenBrain(string owner) => _owner = new OwnerId(owner);

    public NeuronId NeuronNamed(string neuronType, string name) => new(neuronType, Owner, name);

    public Task SendAsync(string synapseTypeName, string neuronType, string name, IReadOnlyDictionary<string, string> values)
    {
        _lastTargetType = neuronType;
        _lastTargetName = name;

        return StimulateAsync(synapseTypeName, NeuronNamed(neuronType, name), values);
    }

    public BrainClient Client => new(SimulationCluster.Grains, Owner);

    public Task ClientFireAsync(string synapseTypeName, string neuronType, string name)
        => Client.FireAsync(neuronType, name, NeuronCatalog.Create(synapseTypeName, EmptyValues));

    public Task ClientFireExpectingRefusalAsync(string synapseTypeName, string neuronType, string name, string targetOwner)
        => CaptureRefusalAsync(() => Client.FireAsync(
            new NeuronId(neuronType, new OwnerId(targetOwner), name),
            NeuronCatalog.Create(synapseTypeName, EmptyValues)));

    public Task<JournalRead> ClientReadJournalAsync(JournalKind kind, string neuronType, string name, long afterSequence)
        => Client.Neuron(neuronType, name).ReadJournalAsync(kind, afterSequence);

    public Task<JournalRead> ClientReadSessionJournalAsync(JournalKind kind)
        => Client.Session.ReadJournalAsync(kind, afterSequence: 0);

    public Task SessionReadOfForeignOwnerExpectingRefusalAsync(JournalKind kind, string neuronType, string name, string targetOwner)
        => CaptureRefusalAsync(() => SimulationCluster.Grains
            .GetGrain<ISessionNeuron>(new NeuronId(ISessionNeuron.GrainTypeName, Owner, "session").ToGrainId())
            .ReadNeuronJournalAsync(new NeuronId(neuronType, new OwnerId(targetOwner), name), kind, afterSequence: 0));

    public Task RawClientSubscriberCountExpectingRefusalAsync(string synapseTypeName, string registryOwner)
        => CaptureRefusalAsync(() => SimulationCluster.Grains
            .GetGrain<ISubscriptionRegistry>(registryOwner)
            .SubscriberCountAsync(NeuronCatalog.SynapseType(synapseTypeName).FullName!));

    public Task RawClientReadJournalExpectingRefusalAsync(JournalKind kind, string neuronType, string name, string targetOwner)
        => CaptureRefusalAsync(() => Neuron(new NeuronId(neuronType, new OwnerId(targetOwner), name))
            .ReadJournalAsync(kind, afterSequence: 0));

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
        => await Client.Neuron(neuronType, name).ReadJournalAsync(kind, afterSequence);

    public static async Task<JournalRead> ReadJournalOfOwnerAsync(
        JournalKind kind,
        string owner,
        string neuronType,
        string name,
        long afterSequence)
        => await new BrainClient(SimulationCluster.Grains, new OwnerId(owner))
            .Neuron(neuronType, name)
            .ReadJournalAsync(kind, afterSequence);

    public async Task RegisterAsync(string neuronType, string name)
        => _ = await Client.Neuron(neuronType, name).ReadJournalAsync(JournalKind.Incoming, afterSequence: 0);

    public Task AwaitHandledAsync(string neuronType, string name, string synapseTypeName)
        => SimulationCluster.Observed.AwaitHandledAsync(NeuronNamed(neuronType, name), synapseTypeName);

    public async Task<int> SettleAsync(JournalKind kind, string neuronType, string name)
    {
        var neuron = Client.Neuron(neuronType, name);
        var quiet = new QuietWatch(SettleQuietPeriod);
        var reference = SimulationCluster.Grains.CreateObjectReference<IJournalObserver>(quiet);

        await neuron.WatchAsync(kind, afterSequence: 0, reference);

        try
        {
            await quiet.AwaitQuietAsync(SettleLimit);
        }
        finally
        {
            await neuron.UnwatchAsync(reference);
        }

        var settled = await neuron.ReadJournalAsync(kind, afterSequence: 0);

        return settled.ResetSnapshot?.RetainedCount ?? settled.Delta.Count;
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
        => CaptureRefusalAsync(() => Driver().SubscribeAsync(
            NeuronCatalog.SynapseType(synapseTypeName).FullName!,
            NeuronNamed(neuronType, name),
            new OwnerId(registryOwner)));

    public Task SubscribeForeignNeuronExpectingRefusalAsync(string neuronType, string name, string synapseTypeName, string subscriberOwner)
        => CaptureRefusalAsync(() => Driver().SubscribeAsync(
            NeuronCatalog.SynapseType(synapseTypeName).FullName!,
            new NeuronId(neuronType, new OwnerId(subscriberOwner), name),
            Owner));

    public Task<int> SubscriberCountAsync(string synapseTypeName)
        => Driver().SubscriberCountAsync(NeuronCatalog.SynapseType(synapseTypeName).FullName!);

    public async Task AwaitBroadcastReceiverAsync(string handlerType, string synapseTypeName)
    {
        if (_lastTargetType is null || _lastTargetName is null)
        {
            throw new SimulationAssertionException(
                "No synapse was sent in this scenario, so there is no broadcast correlation to resolve a receiver from.");
        }

        var expected = NeuronCatalog.SynapseType(synapseTypeName);
        var correlation = await AwaitOutgoingCorrelationAsync(_lastTargetType, _lastTargetName, expected);
        var receiver = NeuronId.BroadcastReceiver(handlerType, Owner, correlation);

        await SimulationCluster.Observed.AwaitHandledAsync(receiver, synapseTypeName);

        var journal = await ReadJournalAsync(JournalKind.Incoming, handlerType, receiver.Name, afterSequence: 0);

        if (!JournalContains(journal, expected))
        {
            throw new SimulationAssertionException(
                $"Expected the incoming journal of {receiver} to contain {synapseTypeName}, but it did not.");
        }
    }

    private async Task<CorrelationId> AwaitOutgoingCorrelationAsync(string neuronType, string name, Type synapseType)
    {
        var limit = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(10);

        while (DateTimeOffset.UtcNow < limit)
        {
            var journal = await ReadJournalAsync(JournalKind.Outgoing, neuronType, name, afterSequence: 0);

            foreach (var delivery in DeliveriesOf(journal))
            {
                if (delivery.Synapse.GetType() == synapseType)
                {
                    return delivery.CorrelationId;
                }
            }

            await Task.Delay(50);
        }

        throw new SimulationAssertionException(
            $"{synapseType.Name} never appeared on the outgoing journal of {neuronType} '{name}' within 10 seconds.");
    }

    private static bool JournalContains(JournalRead journal, Type synapseType)
        => DeliveriesOf(journal).Any(delivery => delivery.Synapse.GetType() == synapseType);

    private static IEnumerable<SynapseDelivery> DeliveriesOf(JournalRead journal)
    {
        if (journal.ResetSnapshot is not null)
        {
            yield break;
        }

        foreach (var delivery in journal.Delta)
        {
            yield return delivery;
        }
    }

    private Task StimulateAsync(string synapseTypeName, NeuronId receiver, IReadOnlyDictionary<string, string> values)
        => Driver().StimulateAsync(receiver, NeuronCatalog.Create(synapseTypeName, values));

    private sealed class QuietWatch(TimeSpan quietPeriod) : IJournalObserver
    {
        private readonly Lock _gate = new();

        private DateTimeOffset _lastPush = DateTimeOffset.UtcNow;

        public Task ObserveAsync(JournalKind kind, JournalRead read)
        {
            lock (_gate)
            {
                _lastPush = DateTimeOffset.UtcNow;
            }

            return Task.CompletedTask;
        }

        public async Task AwaitQuietAsync(TimeSpan limit)
        {
            var deadline = DateTimeOffset.UtcNow + limit;

            while (DateTimeOffset.UtcNow < deadline)
            {
                TimeSpan remaining;

                lock (_gate)
                {
                    remaining = quietPeriod - (DateTimeOffset.UtcNow - _lastPush);
                }

                if (remaining <= TimeSpan.Zero)
                {
                    return;
                }

                await Task.Delay(remaining);
            }

            throw new SimulationAssertionException(
                $"The journal never went quiet for {quietPeriod.TotalMilliseconds:0} ms within {limit.TotalSeconds:0} seconds.");
        }
    }

    private ISimulationNeuron Driver() => SimulationCluster.Grains.GetGrain<ISimulationNeuron>(Id.ToGrainId());

    private static INeuron Neuron(NeuronId id) => SimulationCluster.Grains.GetGrain<INeuron>(id.ToGrainId());
}
