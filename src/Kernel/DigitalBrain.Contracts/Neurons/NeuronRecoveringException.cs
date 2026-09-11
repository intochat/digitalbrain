using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Abstractions.Neurons;

[GenerateSerializer]
[Alias("db.v3.neuron-recovering")]
public sealed class NeuronRecoveringException(NeuronId neuron, string message, Exception? cause = null)
    : InvalidOperationException(message, cause)
{
    [Id(0)] public NeuronId Neuron { get; } = neuron;
}
