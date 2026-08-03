namespace DigitalBrain.Abstractions;

[GenerateSerializer]
[Alias("db.digitalbrain-activated")]
public sealed record DigitalBrainActivated([property: Id(0)] OwnerId Owner) : Synapse;
