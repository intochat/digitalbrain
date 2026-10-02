namespace DigitalBrain.Postgres;

public interface IPostgresTableProvider
{
    Task DefineAsync(string origin, string table, TableDefinition definition, CancellationToken ct);
    Task DropAsync(string origin, string table, CancellationToken ct);
    Task<bool> UpsertAsync(string origin, string table, TableDefinition definition, TableValue[] key, TableValue[] values, CancellationToken ct);
    Task<bool> DeleteAsync(string origin, string table, TableDefinition definition, TableValue[] key, CancellationToken ct);
    Task<TableValue[]?> ReadAsync(string origin, string table, TableDefinition definition, TableValue[] key, CancellationToken ct);
    Task<TableValue[][]> PageAsync(string origin, string table, TableDefinition definition, int offset, int limit, CancellationToken ct);
}
