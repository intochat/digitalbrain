using Orleans;

namespace DigitalBrain.Testing;

[Alias("db.testing.simulation")]
public interface ISimulationNeuron : INeuron
{
    [Alias("Stimulate")]
    Task StimulateAsync(NeuronId receiver, Synapse synapse);
}

internal sealed class SimulationNeuron : Neuron, ISimulationNeuron
{
    public Task StimulateAsync(NeuronId receiver, Synapse synapse)
    {
        ArgumentNullException.ThrowIfNull(synapse);

        var stimulus = synapse with { Metadata = SynapseMetadata.ForSend(Id, receiver) };

        return GrainFactory.GetGrain<INeuron>(receiver.ToGrainId()).DeliverAsync(stimulus);
    }
}
