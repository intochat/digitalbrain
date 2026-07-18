using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.Runtime.Runtime
{
    [GenerateSerializer]
    public sealed record Started : Synapse;
}

namespace DigitalBrain.Kernel
{
    [GenerateSerializer]
    public sealed record Loaded : Synapse;
}
