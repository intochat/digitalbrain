using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;

namespace DigitalBrain.Testing;

[Alias("db.testing.simulation")]
public interface ISimulationNeuron : INeuron
{
    [Alias("Stimulate")]
    Task StimulateAsync(NeuronId receiver, Synapse synapse);

    [Alias("StimulateTwice")]
    Task StimulateTwiceAsync(NeuronId receiver, Synapse synapse);
}

internal sealed class SimulationNeuron : Neuron, ISimulationNeuron
{
    public async Task StimulateAsync(NeuronId receiver, Synapse synapse)
    {
        ArgumentNullException.ThrowIfNull(synapse);

        var delivery = await FireAsync(synapse, []);

        await GrainFactory.GetGrain<INeuron>(receiver.ToGrainId()).DeliverAsync(delivery);
    }

    public async Task StimulateTwiceAsync(NeuronId receiver, Synapse synapse)
    {
        ArgumentNullException.ThrowIfNull(synapse);

        var delivery = await FireAsync(synapse, []);
        var target = GrainFactory.GetGrain<INeuron>(receiver.ToGrainId());

        await target.DeliverAsync(delivery);
        await target.DeliverAsync(delivery);
    }
}
