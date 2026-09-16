namespace DigitalBrain.Abstractions.Signals;

[GenerateSerializer]
[Alias("db.neuron-busy")]
public sealed class NeuronBusyException(string message) : InvalidOperationException(message);
