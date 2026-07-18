namespace DigitalBrain.Runtime.Runtime;

[GenerateSerializer]
public sealed record DecayPolicy(
    [property: Id(0)] TimeSpan? MaxAge = null,
    [property: Id(1)] int? MaxCount = null
);
