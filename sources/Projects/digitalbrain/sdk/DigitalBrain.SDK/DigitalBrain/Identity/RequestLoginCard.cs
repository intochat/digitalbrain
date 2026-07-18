using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.SDK.DigitalBrain.Identity;

[GenerateSerializer]
public sealed record RequestLoginCard([property: Id(1)] string UserId
) : Synapse;
