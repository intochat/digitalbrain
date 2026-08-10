using System.ComponentModel;

namespace DigitalBrain.Abstractions;

[GenerateSerializer]
[Alias("db.disconnect")]
[Description("Remove a synapse connection by its identity")]
public sealed record Disconnect([property: Id(0)] Guid ConnectionId) : RequestSynapse<Disconnected>;

[GenerateSerializer]
[Alias("db.disconnected")]
[Description("A synapse connection was removed")]
public sealed record Disconnected([property: Id(0)] Guid ConnectionId) : Synapse;
