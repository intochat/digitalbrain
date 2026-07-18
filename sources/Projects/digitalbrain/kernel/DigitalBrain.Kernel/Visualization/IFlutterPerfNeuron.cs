using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.Kernel.Visualization;

// Cluster-singleton kernel-side neuron grain keyed by Guid.Empty — one
// projection per cluster; the ticker, hint broadcaster, and implicit-stream
// subscriber all converge on this activation.
public interface IFlutterPerfNeuron : INeuron
{
    Task Tick();
}
