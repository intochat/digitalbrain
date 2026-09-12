namespace DigitalBrain.ClickHouse;

// Carries the server's own error text so the agent can correct a wrong column or function name.
[GenerateSerializer, Alias("db.clickhouse.query-failed")]
public sealed class ClickHouseQueryException(string message) : InvalidOperationException(message);

[GenerateSerializer, Alias("db.clickhouse.unavailable")]
public sealed class ClickHouseUnavailableException(string message) : InvalidOperationException(message);
