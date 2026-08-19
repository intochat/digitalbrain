namespace DigitalBrain.Core;

// Public: the keyed-DI service key Entity<TState>/Neuron resolve their TimeProvider against.
// Fake-clock tests (in-repo or against the packable Testing SDK) register a TimeProvider under
// this exact key to control brain recency/tally learning deterministically.
public static class NeuronTime
{
    public static object ServiceKey { get; } = new();
}
