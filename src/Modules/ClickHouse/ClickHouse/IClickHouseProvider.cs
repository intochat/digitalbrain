using DigitalBrain.ClickHouse.Query;

namespace DigitalBrain.ClickHouse;

// The interface between the neurons and a ClickHouse server.
internal interface IClickHouseProvider
{
    string ProviderName { get; }

    Task<ClickHouseQueryResult> QueryAsync(string sql, int maxRows, CancellationToken cancellationToken);

    Task<IReadOnlyList<ClickHouseColumn>> DescribeAsync(string sql, CancellationToken cancellationToken);

    Task<ClickHouseSchema> ReadSchemaAsync(string? table, CancellationToken cancellationToken);

    Task<ClickHouseConnection> PingAsync(CancellationToken cancellationToken);
}