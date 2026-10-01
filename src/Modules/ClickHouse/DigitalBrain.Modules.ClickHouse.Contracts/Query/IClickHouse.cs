using DigitalBrain.Contracts;
using Orleans.Concurrency;

namespace DigitalBrain.ClickHouse.Query;

[Alias("clickhouse")]
public interface IClickHouse : INeuron
{
    [ReadOnly]
    Task<ClickHouseQueryResult> Query(ClickHouseQuery query, CancellationToken cancellationToken = default);

    [ReadOnly]
    Task<ClickHouseSchema> ReadSchema(ReadClickHouseSchema query, CancellationToken cancellationToken = default);

    [ReadOnly]
    Task<ClickHouseConnection> ReadConnection(CancellationToken cancellationToken = default);
}
