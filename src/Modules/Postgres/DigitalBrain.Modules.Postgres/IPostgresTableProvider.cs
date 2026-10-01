namespace DigitalBrain.Postgres;

public interface IPostgresTableProvider
{
    Task DefineAsync(string table, TableDefinition definition, CancellationToken ct);
    Task<bool> UpsertAsync(string table, TableDefinition definition, TableValue[] key, TableValue[] values, CancellationToken ct);
    Task<bool> DeleteAsync(string table, TableDefinition definition, TableValue[] key, CancellationToken ct);
    Task<TableValue[]?> ReadAsync(string table, TableDefinition definition, TableValue[] key, CancellationToken ct);
    Task<TableValue[][]> PageAsync(string table, TableDefinition definition, int offset, int limit, CancellationToken ct);
}
