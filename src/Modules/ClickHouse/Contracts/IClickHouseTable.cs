using DigitalBrain.Abstractions.Commands;
using DigitalBrain.UI;

namespace DigitalBrain.ClickHouse;

[Alias("clickhouse.table")]
public interface IClickHouseTable : ITable
{
    /// <summary>Schedules creation of a table whose rows are served live from a ClickHouse query.</summary>
    [Alias("create-query")]
    Task<Accepted<string>> CreateFromQuery(CreateQueryTableCommand command, CancellationToken cancellationToken = default);
}
