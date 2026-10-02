namespace DigitalBrain;

// A signal is a typed fact that crosses a synapse. Publisher is stamped by the kernel when the
// source neuron publishes it — never supplied by the sender; default only for signals born
// outside a neuron. Facts are values: a signal must serialize as pure data on any runtime.
public record Signal
{
    public NeuronId Publisher { get; init; }
}
