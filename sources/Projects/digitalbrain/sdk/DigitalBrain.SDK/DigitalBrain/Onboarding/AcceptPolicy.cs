using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.SDK.DigitalBrain.Onboarding;

[GenerateSerializer]
public sealed record AcceptPolicy([property: Id(1)] string UserId,
    [property: Id(2)] string Version
) : Synapse;
