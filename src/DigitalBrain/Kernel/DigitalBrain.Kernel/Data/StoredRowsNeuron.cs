using DigitalBrain;
using DigitalBrain.Contracts;
using DigitalBrain.Contracts.Data;
using DigitalBrain.Kernel;
using Orleans.Concurrency;

namespace DigitalBrain.Kernel.Data;

[GenerateSerializer, Alias("data.stored-rows-state")]
public sealed class StoredRowsState
{
    [Id(0)] public RowColumn[] Columns { get; set; } = [];
    [Id(1)] public Row[] Rows { get; set; } = [];
}

[GrainType("data.stored-rows")]
internal sealed class StoredRowsNeuron(
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<StoredRowsState> store)
    : Neuron<StoredRowsState>(store), IStoredRows
{
    public Task Replace(RowSchema schema, Row[] rows)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(rows);
        if (schema.Columns.Length == 0) { throw new ArgumentException("A stored table needs a column."); }
        foreach (var row in rows)
        {
            ArgumentNullException.ThrowIfNull(row);
            if (row.Values.Length != schema.Columns.Length)
            {
                throw new ArgumentException("A row must have one value per column.");
            }
        }

        var next = new StoredRowsState { Columns = [.. schema.Columns], Rows = [.. rows] };
        return Save(next, new RowsReplaced(this.GetPrimaryKeyString(), rows.Length));
    }

    [ReadOnly]
    public Task<RowSchema> ReadSchema() => Task.FromResult(new RowSchema([.. Snapshot.Columns]));

    [ReadOnly]
    public Task<SourceCapabilities> ReadCapabilities() => Task.FromResult(new SourceCapabilities());

    [ReadOnly]
    public Task<RowPage> Read(RowQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        return Task.FromResult(RowQueryEvaluator.Evaluate(new RowSchema([.. Snapshot.Columns]), [.. Snapshot.Rows], query));
    }
}
