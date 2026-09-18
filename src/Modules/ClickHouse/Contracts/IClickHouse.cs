using DigitalBrain.Abstractions.Neurons;
using Orleans.Concurrency;

namespace DigitalBrain.ClickHouse;

[Alias("clickhouse")]
public interface IClickHouse : INeuron
{
    /// <summary>Runs one read-only SELECT with server-side caps and returns typed rows.</summary>
    [ReadOnly, Alias("query")]
    Task<ClickHouseQueryResult> Query(ClickHouseQuery query, CancellationToken cancellationToken = default);

    /// <summary>Reads tables and columns of the configured database; omit Table for the index.</summary>
    [ReadOnly, Alias("schema")]
    Task<ClickHouseSchema> ReadSchema(ReadClickHouseSchema query, CancellationToken cancellationToken = default);

    [ReadOnly, Alias("connection")]
    Task<ClickHouseConnection> ReadConnection(CancellationToken cancellationToken = default);
}
