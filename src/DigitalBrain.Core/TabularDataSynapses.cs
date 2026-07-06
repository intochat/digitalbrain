namespace DigitalBrain.Core;

// Fired after a dropped Excel/CSV file is parsed server-side (see DigitalBrain.Kernel.TabularData.TabularDataParser).
// Headers/Rows/ColumnStats travel as JSON strings so this synapse stays a plain flat record for Orleans transport.
[GenerateSerializer]
public record TabularDataIngested(
    string FileName,
    string HeadersJson,
    string RowsJson,
    string ColumnStatsJson,
    string? ClientId = null,
    string? WorkspaceId = null) : Synapse(nameof(TabularDataIngested), DateTimeOffset.UtcNow);
