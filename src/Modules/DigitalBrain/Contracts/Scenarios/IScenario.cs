using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Abstractions.Synapses;
using Orleans.Concurrency;

namespace DigitalBrain.Abstractions.Scenarios;

// A scenario is not a neuron. It is the program: a graph of synapses that route signals
// from object grains to object grains. Identity is a string name (rates-daily).
[Alias("db.v3.scenario")]
public interface IScenario : IGrainWithStringKey
{
    [Alias(nameof(Bind))]
    Task Bind(NeuronId source, string signalType, NeuronId target);

    [Alias(nameof(Unbind))]
    Task Unbind(NeuronId source, string signalType, NeuronId target);

    [ReadOnly, Alias(nameof(Read))]
    Task<IReadOnlyList<Synapse>> Read();

    // For each matching synapse, Deliver the same envelope to the target neuron.
    [Alias(nameof(Route))]
    Task<int> Route(SignalDelivery delivery, CancellationToken cancellationToken = default);
}
