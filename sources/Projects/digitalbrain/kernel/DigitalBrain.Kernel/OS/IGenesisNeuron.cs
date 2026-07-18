using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.Kernel.OS;

public interface IGenesisNeuron : INeuron, IGrainWithGuidKey
{
    Task InitializeGenesisAsync(InitializeGenesis synapse);
}
