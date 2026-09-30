namespace DigitalBrain.Postgres;

// Carries a safe error with SQLSTATE, excluding server details and connection credentials.
[GenerateSerializer, Alias("db.postgres.query-failed")]
public sealed class PostgresQueryException(string message) : InvalidOperationException(message);

[GenerateSerializer, Alias("db.postgres.unavailable")]
public sealed class PostgresUnavailableException(string message) : InvalidOperationException(message);
