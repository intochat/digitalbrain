using DigitalBrain.Contracts;
using DigitalBrain.Contracts.Data;
using DigitalBrain.Core;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Table.Signals;
using Orleans.Concurrency;
using Orleans.Runtime;

namespace DigitalBrain.Flutter.Table;

[GrainType(UIVocabulary.TableType)]
internal sealed class TableNeuron([PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<TableState> store)
    : Neuron<TableState>(store), ITable
{
    public Task Replace(string title, IReadOnlyList<TableColumn> columns, IReadOnlyList<IReadOnlyList<string>> rows)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(rows);
        var next = Snapshot;
        next.Name = this.GetPrimaryKeyString();
        next.Version++;
        next.Title = title.Trim();
        next.Columns = [.. columns];
        next.Rows = [.. rows.Select(row => row.ToList())];
        next.SourceNeuronId = null;
        next.Query = null;
        return Save(next, new TableChanged(this.GetPrimaryKeyString(), next.Version));
    }

    public Task SetView(string sort, string filter)
    {
        ArgumentNullException.ThrowIfNull(sort);
        ArgumentNullException.ThrowIfNull(filter);
        var next = Snapshot;
        next.Name = this.GetPrimaryKeyString();
        next.Version++;
        next.Sort = sort;
        next.Filter = filter;
        return Save(next, new TableChanged(this.GetPrimaryKeyString(), next.Version));
    }

    public Task Bind(string sourceNeuronId, RowQuery query)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceNeuronId);
        ArgumentNullException.ThrowIfNull(query);
        var next = Snapshot;
        next.Name = this.GetPrimaryKeyString();
        next.Version++;
        next.SourceNeuronId = sourceNeuronId;
        next.Query = query.Degrade(new SourceCapabilities());
        next.Columns = [];
        next.Rows = [];
        return Save(next, new TableChanged(this.GetPrimaryKeyString(), next.Version));
    }

    [ReadOnly]
    public async Task<TableState> Read()
    {
        var state = Snapshot;
        if (string.IsNullOrEmpty(state.SourceNeuronId))
        {
            state.Name = this.GetPrimaryKeyString();
            return state;
        }

        var source = RowSourceAddress.Open(GrainFactory, state.SourceNeuronId);
        var capabilities = await source.ReadCapabilities();
        var page = await source.Read((state.Query ?? new RowQuery()).Degrade(capabilities));
        return new TableState
        {
            Name = this.GetPrimaryKeyString(),
            Version = state.Version,
            Title = state.Title,
            Columns = [.. page.Columns.Select(column => new TableColumn(column.Name, column.Name))],
            Rows = [.. page.Rows.Select(row => row.Values.ToList())],
            Sort = state.Sort,
            Filter = state.Filter,
            SourceNeuronId = state.SourceNeuronId,
            Query = state.Query,
        };
    }
}
