namespace DigitalBrain.Postgres;

internal sealed record LegacyPostgresTable(string Id, string Owner);

internal interface IPostgresLegacyTables
{
    Task<LegacyPostgresTable[]> Read(CancellationToken ct);
}
