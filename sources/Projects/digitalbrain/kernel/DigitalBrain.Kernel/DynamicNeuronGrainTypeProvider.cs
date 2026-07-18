using DigitalBrain.Runtime;
using Orleans.Metadata;

namespace DigitalBrain.Kernel;

public sealed class DynamicNeuronGrainTypeProvider : IGrainTypeProvider
{
    public bool TryGetGrainType(Type type, out GrainType grainType)
    {
        if (type == typeof(DynamicNeuronGrain))
        {
            grainType = GrainType.Create("DynamicNeuronGrain");
            return true;
        }

        grainType = default;
        return false;
    }
}
