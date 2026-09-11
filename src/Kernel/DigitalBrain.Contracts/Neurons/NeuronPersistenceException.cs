using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Abstractions.Neurons;

[GenerateSerializer]
[Alias("db.v3.neuron-persistence")]
public sealed class NeuronPersistenceException(NeuronId neuron, string message, Exception cause)
    : InvalidOperationException(message, cause)
{
    [Id(0)] public NeuronId Neuron { get; } = neuron;
}
