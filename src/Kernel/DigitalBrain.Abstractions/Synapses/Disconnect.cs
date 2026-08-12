namespace DigitalBrain.Abstractions;

[GenerateSerializer]
[Alias("db.disconnect")]
public sealed record Disconnect([property: Id(0)] Guid ConnectionId) : RequestSynapse<Disconnected>;
