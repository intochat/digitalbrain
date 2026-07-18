using DigitalBrain.Runtime;

namespace DigitalBrain.Kernel.Creator;

public interface IGeneratedNeuronStore
{
    Task WriteAsync(DynamicNeuronSpec spec, string stepsCode);
}
