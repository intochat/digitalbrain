using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.Runtime.Runtime;

[GenerateSerializer]
public sealed record ConfigureAspireResource(
    [property: Id(0)] string ResourceName,
    [property: Id(1)] string ResourceType,
    [property: Id(2)] Dictionary<string, string> Config
) : Synapse;
