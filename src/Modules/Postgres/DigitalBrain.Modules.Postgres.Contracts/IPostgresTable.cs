using DigitalBrain.Contracts;

namespace DigitalBrain.Postgres;

// Use a unique grain key per installed table. First Define binds it permanently to the
// stamped brain and app; every subsequent operation requires that same caller scope.
[Alias("postgres.table"), Orleans.Metadata.DefaultGrainType("postgres.table")]
public interface IPostgresTable : INeuron
{
    Task<PostgresTableDefinition> Define(TableDefinition definition);
    Task<bool> Upsert(TableValue[] key, TableValue[] values);
    Task<bool> Delete(TableValue[] key);
    Task<TableValue[]?> Read(TableValue[] key);
    Task<TableValue[][]> Page(int offset = 0, int limit = 200);
}

[GenerateSerializer, Alias("postgres.table.column")]
public sealed record TableColumn([property: Id(0)] string Name, [property: Id(1)] string Type);

[GenerateSerializer, Alias("postgres.table.definition")]
public sealed record TableDefinition([property: Id(0)] TableColumn[] Columns, [property: Id(1)] string[] PrimaryKey);

// Json is a single JSON value, including quotes for text. Null is allowed outside the key.
[GenerateSerializer, Alias("postgres.table.value")]
public sealed record TableValue([property: Id(0)] string Column, [property: Id(1)] string Json);

[GenerateSerializer, Alias("postgres.table.accepted")]
public sealed record PostgresTableDefinition([property: Id(0)] string Table, [property: Id(1)] TableDefinition Definition,
    [property: Id(2)] long Revision);

[GenerateSerializer, Alias("postgres.table.defined")]
public sealed record TableDefined([property: Id(0)] string Table, [property: Id(1)] long Revision) : Signal;

[GenerateSerializer, Alias("postgres.table.upserted")]
public sealed record RowUpserted([property: Id(0)] string Table, [property: Id(1)] TableValue[] Key) : Signal;

[GenerateSerializer, Alias("postgres.table.deleted")]
public sealed record RowDeleted([property: Id(0)] string Table, [property: Id(1)] TableValue[] Key) : Signal;
