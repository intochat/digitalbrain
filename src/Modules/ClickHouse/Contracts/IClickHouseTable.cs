using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Descriptors;
using DigitalBrain.Flutter;

namespace DigitalBrain.ClickHouse;

[Alias("clickhouse.table")]
public interface IClickHouseTable : ITable
{
    /// <summary>Schedules creation of a table whose rows are served live from a ClickHouse query.</summary>
    [Alias("create-query")]
    [NeuronTool]
    Task<Accepted<string>> CreateFromQuery(CreateQueryTableCommand command, CancellationToken cancellationToken = default);
}
