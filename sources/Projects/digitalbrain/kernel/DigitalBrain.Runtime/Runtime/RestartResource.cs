using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.Runtime.Runtime;

[GenerateSerializer]
public sealed record RestartResource(
    [property: Id(0)] string ResourceName
) : Synapse;
