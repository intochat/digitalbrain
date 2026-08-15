namespace DigitalBrain.Abstractions.Graph;

[GenerateSerializer]
[Alias("db.disconnect")]
public sealed record Disconnect([property: Id(0)] Guid ConnectionId) : RequestSynapse<Disconnected>;
