namespace DigitalBrain.Postgres;

internal sealed record LegacyPostgresTable(string Id, string Owner);
internal sealed record LegacyPostgresScan(LegacyPostgresTable[] Tables, string[] SkippedBlobs);

internal interface IPostgresLegacyTables
{
    Task<LegacyPostgresScan> Read(CancellationToken ct);
}
