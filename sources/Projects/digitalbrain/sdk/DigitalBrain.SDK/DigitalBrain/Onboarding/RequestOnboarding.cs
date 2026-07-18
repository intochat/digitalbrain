using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.SDK.DigitalBrain.Onboarding;

[GenerateSerializer]
public sealed record RequestOnboarding([property: Id(1)] string UserId,
    [property: Id(2)] string? AcceptedVersion
) : Synapse;
