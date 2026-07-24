using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;

namespace DigitalBrain.Testing;

[Alias("db.testing.simulation")]
[ClientEntryPoint]
public partial interface ISimulationNeuron : INeuron
{
    [Alias("Stimulate")]
    Task StimulateAsync(NeuronId receiver, Synapse synapse);

    [Alias("StimulateTwice")]
    Task StimulateTwiceAsync(NeuronId receiver, Synapse synapse);

    [Alias("Subscribe")]
    Task SubscribeAsync(string synapseType, NeuronId subscriber, OwnerId registryOwner);

    [Alias("SubscriberCount")]
    Task<int> SubscriberCountAsync(string synapseType);
}

internal sealed class SimulationNeuron : Neuron, ISimulationNeuron
{
    public async Task StimulateAsync(NeuronId receiver, Synapse synapse)
    {
        ArgumentNullException.ThrowIfNull(synapse);

        var delivery = await FireAsync(synapse, []);

        await GrainFactory.GetGrain<INeuron>(receiver.ToGrainId()).Deliver(delivery);
    }

    public async Task StimulateTwiceAsync(NeuronId receiver, Synapse synapse)
    {
        ArgumentNullException.ThrowIfNull(synapse);

        var delivery = await FireAsync(synapse, []);
        var target = GrainFactory.GetGrain<INeuron>(receiver.ToGrainId());

        await target.Deliver(delivery);
        await target.Deliver(delivery);
    }

    public Task SubscribeAsync(string synapseType, NeuronId subscriber, OwnerId registryOwner)
        => GrainFactory.GetGrain<ISubscriptionRegistry>(registryOwner.Value).Register(synapseType, subscriber);

    public Task<int> SubscriberCountAsync(string synapseType)
        => GrainFactory.GetGrain<ISubscriptionRegistry>(Id.Owner.Value).SubscriberCount(synapseType);
}
