using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.SDK.DigitalBrain.Onboarding;

[GenerateSerializer]
public sealed record OnboardingResult([property: Id(1)] bool NeedsAccept,
    [property: Id(2)] string CurrentVersion
) : Synapse;
