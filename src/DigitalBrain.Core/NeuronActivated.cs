namespace DigitalBrain.Core;

[GenerateSerializer]
[Alias("DigitalBrain.Core.NeuronActivated")]
public record NeuronActivated(NeuronId Neuron) : Synapse(nameof(NeuronActivated), DateTimeOffset.UtcNow);
