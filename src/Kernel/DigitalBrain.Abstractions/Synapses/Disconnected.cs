namespace DigitalBrain.Abstractions;

[GenerateSerializer]
[Alias("db.disconnected")]
public sealed record Disconnected([property: Id(0)] Guid ConnectionId) : Synapse;

