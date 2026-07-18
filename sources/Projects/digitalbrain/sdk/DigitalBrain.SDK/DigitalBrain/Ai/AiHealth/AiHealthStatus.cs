namespace DigitalBrain.SDK.DigitalBrain.Ai.AiHealth;

[GenerateSerializer]
public readonly record struct AiHealthStatus(
    [property: Id(0)] bool Live,
    [property: Id(1)] string Reason,
    [property: Id(2)] string Model);
