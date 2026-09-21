namespace DigitalBrain.ClickHouse.Tables;

// Raised when a saved view or page request violates the table rules; crosses the grain boundary.
[GenerateSerializer, Alias("db.clickhouse.table-invalid")]
public sealed class ClickHouseTableValidationException(string message) : ArgumentException(message);

// Raised when the SELECT behind a live table cannot serve rows (refused or unreachable).
[GenerateSerializer, Alias("db.clickhouse.table-source-failed")]
public sealed class ClickHouseTableSourceException(string message) : InvalidOperationException(message);