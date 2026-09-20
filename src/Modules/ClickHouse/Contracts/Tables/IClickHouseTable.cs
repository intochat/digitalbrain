using DigitalBrain.Contracts;
using Orleans.Concurrency;

namespace DigitalBrain.ClickHouse.Tables;

// An IClickHouseTable serves rows live from a saved read-only SELECT. The saved view (columns,
// filters, sort, visible columns, revision) is the conversation's working set; every read
// compiles it into SQL and renders it to whoever watches the neuron.
[Alias("clickhouse.table")]
public interface IClickHouseTable : INeuron
{
    /// <summary>Creates the table from a saved read-only SELECT and renders it.</summary>
    Task<ClickHouseTableView> CreateFromQuery(CreateQueryTable table);

    /// <summary>Replaces the saved view at an expected revision and renders it.</summary>
    Task<ClickHouseTableView> Update(UpdateClickHouseTableView view);

    [ReadOnly]
    Task<ClickHouseTableSnapshot?> Read(ReadClickHouseTable query);

    [ReadOnly]
    Task<ClickHouseTableSummary?> ReadSummary();

    /// <summary>Re-publishes the outward card signal without touching the stored view.</summary>
    Task<ClickHouseTableSummary?> Render();
}
