namespace DigitalBrain.SDK.DigitalBrain.Onboarding.Onboarding;

[GenerateSerializer]
public sealed record PolicyAcceptanceRecord(
    [property: Id(0)] string UserId,
    [property: Id(1)] string Version);
