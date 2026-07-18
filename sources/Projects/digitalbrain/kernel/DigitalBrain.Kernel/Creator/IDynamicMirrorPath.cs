using DigitalBrain.Runtime;

namespace DigitalBrain.Kernel.Creator;

public interface IDynamicMirrorPath
{
    string For(NeuronId id);
}
