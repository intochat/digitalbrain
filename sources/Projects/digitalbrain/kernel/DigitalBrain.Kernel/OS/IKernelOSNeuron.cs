using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.Kernel.OS;

public interface IKernelOSNeuron : INeuron, IGrainWithGuidKey
{
    Task BootSystemAsync(BootSystem synapse);
}
