namespace DigitalBrain.Capabilities.Connections;

[Signal("connection.connected")]
public sealed record ConnectionConnected(string ConnectorId, string ConnectionId, string AuthMethodId, string? Account, DateTimeOffset? ProbedAt = null) : Signal;

[Signal("connection.expired")]
public sealed record ConnectionExpired(string ConnectorId, string ConnectionId, DateTimeOffset? ProbedAt = null) : Signal;

[Signal("connection.failing")]
public sealed record ConnectionFailing(string ConnectorId, string ConnectionId, string Detail, DateTimeOffset? ProbedAt = null) : Signal;

[Signal("connection.adopted")]
public sealed record ConnectionAdopted(string ConnectorId, string ConnectionId, string AuthMethodId, string? Account) : Signal;

[Signal("connection.disconnected")]
public sealed record ConnectionDisconnected(string ConnectorId, string ConnectionId) : Signal;

[Signal("connection.shared")]
public sealed record ConnectionShared(string ConnectorId, string ConnectionId, string Recipient) : Signal;

[Signal("connection.unshared")]
public sealed record ConnectionUnshared(string ConnectorId, string ConnectionId, string Recipient) : Signal;

// A connection's state changed without a status change (a sharing recorded or withdrawn, a module's own bookkeeping): a plain
// neuron saves only with a signal (06-08).
[Signal("connection.updated")]
public sealed record ConnectionUpdated(string ConnectorId, string ConnectionId) : Signal;
