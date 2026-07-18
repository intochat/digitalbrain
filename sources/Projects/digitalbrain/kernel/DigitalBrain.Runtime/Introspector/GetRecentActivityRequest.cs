using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.Runtime.Introspector;

[GenerateSerializer]
public sealed record GetRecentActivityRequest([property: Id(1)] string UserId,
    [property: Id(2)] TimeSpan Since
) : Synapse;
