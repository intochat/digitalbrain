namespace DigitalBrain.Contracts;

[GenerateSerializer, Alias("neuron.signal")]
public record Signal;

[GenerateSerializer, Alias("neuron.activated")]
public sealed record NeuronActivated([property: Id(0)] string NeuronId) : Signal;

[GenerateSerializer, Alias("neuron.deactivated")]
public sealed record NeuronDeactivated(
    [property: Id(0)] string NeuronId,
    [property: Id(1)] string Reason) : Signal;
