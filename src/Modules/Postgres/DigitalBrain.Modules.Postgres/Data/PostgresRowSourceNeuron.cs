using DigitalBrain.Contracts.Data;
using DigitalBrain.Core;
using DigitalBrain.Sdk.Data;
using Orleans.Concurrency;

namespace DigitalBrain.Postgres;

[GrainType("data.postgres-rows")]
internal sealed class PostgresRowSourceNeuron(IGrainFactory grains) : Neuron, IPostgresRows
{
    [ReadOnly]
    public async Task<RowSchema> ReadSchema()
    {
        var table = this.GetPrimaryKeyString();
        var schema = await grains.GetGrain<IPostgres>(table).ReadSchema(new ReadPostgresSchema(table));
        return new RowSchema([.. Columns(schema, table).Select(column => new RowColumn(column.Name, column.TableType))]);
    }

    [ReadOnly]
    public Task<SourceCapabilities> ReadCapabilities() => Task.FromResult(new SourceCapabilities());

    [ReadOnly]
    public async Task<RowPage> Read(RowQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        var schema = await ReadSchema();
        var sql = RowQuerySql.Compile(this.GetPrimaryKeyString(), schema, query);
        var result = await grains.GetGrain<IPostgres>(this.GetPrimaryKeyString()).Query(new PostgresQuery(sql, query.Limit));
        return new RowPage(
            [.. result.Columns.Select(column => new RowColumn(column.Name, column.TableType))],
            [.. result.Rows.Select(row => new Row([.. row.Select(RowCells.FromJson)]))],
            result.Truncated);
    }

    private static IReadOnlyList<PostgresColumn> Columns(PostgresSchema schema, string table)
    {
        var match = schema.Tables.FirstOrDefault(candidate => string.Equals(candidate.Name, table, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"Table '{table}' was not found.");
        return match.Columns;
    }
}
