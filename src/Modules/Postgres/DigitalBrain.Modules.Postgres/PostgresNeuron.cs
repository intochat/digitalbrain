using DigitalBrain.Core;
using Orleans.Concurrency;

namespace DigitalBrain.Postgres;

[GrainType("postgres")]
internal sealed class PostgresNeuron(IPostgresProvider provider) : Neuron, IPostgres
{
    [ReadOnly]
    public Task<PostgresQueryResult> Query(PostgresQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.MaxRows is < 1 or > PostgresQuery.MaxRowsLimit)
        {
            throw new PostgresQueryException($"maxRows must be between 1 and {PostgresQuery.MaxRowsLimit}.");
        }
        string sql;
        try { sql = PostgresQueryGuard.Normalize(query.Sql); }
        catch (ArgumentException error) { throw new PostgresQueryException(error.Message); }
        return provider.QueryAsync(sql, query.MaxRows, CancellationToken.None);
    }

    [ReadOnly]
    public Task<PostgresSchema> ReadSchema(ReadPostgresSchema query)
    {
        ArgumentNullException.ThrowIfNull(query);
        var table = string.IsNullOrWhiteSpace(query.Table) ? null : query.Table.Trim();
        if (table is not null && (table.Length > 255 || !table.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '.')))
        {
            throw new PostgresQueryException("table must be a plain table name, optionally prefixed with its schema.");
        }
        return provider.ReadSchemaAsync(table, CancellationToken.None);
    }

    [ReadOnly]
    public Task<PostgresConnection> ReadConnection() => provider.PingAsync(CancellationToken.None);
}
