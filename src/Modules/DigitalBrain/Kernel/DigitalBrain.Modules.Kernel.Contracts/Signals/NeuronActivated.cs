namespace DigitalBrain.Contracts.Signals;

[GenerateSerializer, Alias("brain.neuron-activated")]
public sealed record NeuronActivated : NeuronActivity;
