namespace DigitalBrain.Abstractions.Messaging;

[GenerateSerializer]
[Alias("db.digitalbrain-activated")]
public sealed record DigitalBrainActivated([property: Id(0)] OwnerId Owner) : Synapse;
