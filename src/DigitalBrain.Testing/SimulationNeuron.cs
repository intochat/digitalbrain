using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;
using Orleans.Concurrency;

namespace DigitalBrain.Testing;

[Alias("db.testing.simulation")]
public interface ISimulationNeuron : INeuron
{
    [AlwaysInterleave]
    [Alias("Stimulate")]
    Task StimulateAsync(NeuronId receiver, Synapse synapse);

    [AlwaysInterleave]
    [Alias("StimulateTwice")]
    Task StimulateTwiceAsync(NeuronId receiver, Synapse synapse);
}

internal sealed class SimulationNeuron : Neuron, ISimulationNeuron
{
    public Task StimulateAsync(NeuronId receiver, Synapse synapse)
    {
        ArgumentNullException.ThrowIfNull(synapse);

        var stimulus = synapse with { Metadata = SynapseMetadata.ForSend(Id, receiver) };

        return GrainFactory.GetGrain<INeuron>(receiver.ToGrainId()).DeliverAsync(stimulus);
    }

    public async Task StimulateTwiceAsync(NeuronId receiver, Synapse synapse)
    {
        ArgumentNullException.ThrowIfNull(synapse);

        var stimulus = synapse with { Metadata = SynapseMetadata.ForSend(Id, receiver) };
        var target = GrainFactory.GetGrain<INeuron>(receiver.ToGrainId());

        await target.DeliverAsync(stimulus);
        await target.DeliverAsync(stimulus);
    }
}
