using System.Text.RegularExpressions;
using DigitalBrain.Contracts;
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

    [ReadOnly] public Task<TableState> Read() { Snapshot.Name = this.GetPrimaryKeyString(); return Task.FromResult(Snapshot); }
}

