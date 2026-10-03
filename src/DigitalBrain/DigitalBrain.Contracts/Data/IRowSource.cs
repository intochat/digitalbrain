using Orleans.Concurrency;

namespace DigitalBrain.Contracts.Data;

// One read shape for every table: a Postgres table, a Supabase table, a stored-rows table.
[Alias("data.row-source"), Orleans.Metadata.DefaultGrainType("data.stored-rows")]
public interface IRowSource : INeuron
{
    [ReadOnly] Task<RowSchema> ReadSchema();

    [ReadOnly] Task<SourceCapabilities> ReadCapabilities();

    [ReadOnly] Task<RowPage> Read(RowQuery query);
}

// The behavior-fed table. Its grain type is the default IRowSource, so brain.Get<IRowSource>(id) addresses it.
[Alias("data.stored-rows"), Orleans.Metadata.DefaultGrainType("data.stored-rows")]
public interface IStoredRows : IRowSource
{
    Task Replace(RowSchema schema, Row[] rows);
}

[GenerateSerializer, Alias("data.rows-replaced")]
public sealed record RowsReplaced([property: Id(0)] string Name, [property: Id(1)] int Count) : Signal;
