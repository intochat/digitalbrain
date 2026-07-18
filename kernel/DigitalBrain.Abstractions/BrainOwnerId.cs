namespace DigitalBrain;

[GenerateSerializer]
[Alias(nameof(BrainOwnerId))]
public readonly record struct BrainOwnerId([property: Id(0)] string Value);
