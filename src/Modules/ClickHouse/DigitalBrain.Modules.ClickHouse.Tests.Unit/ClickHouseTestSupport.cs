using DigitalBrain.ClickHouse;
using DigitalBrain.ClickHouse.Query;

namespace DigitalBrain.Tests;

// In-memory IClickHouseProvider so the neuron tests never touch a real server.
internal sealed class FakeClickHouseProvider : IClickHouseProvider
{
    private readonly Dictionary<string, IReadOnlyList<ClickHouseColumn>> _schemas = new(StringComparer.Ordinal);

    public string ProviderName => "Fake";
    public bool Connected { get; set; } = true;
    public string Database => "digitalbrain";

    public FakeClickHouseProvider WithColumns(string sql, params ClickHouseColumn[] columns)
    {
        _schemas[sql] = columns;
        return this;
    }

    public Task<ClickHouseQueryResult> QueryAsync(string sql, int maxRows, CancellationToken cancellationToken)
    {
        ClickHouseQueryGuard.Validate(sql);
        var columns = new[] { new ClickHouseColumn("value", "UInt8", ClickHouseTypeMap.Number) };
        IReadOnlyList<IReadOnlyList<string>> rows =
        [
            ["42"],
        ];
        return Task.FromResult(new ClickHouseQueryResult(columns, rows, 1, false, 1.5));
    }

    public Task<IReadOnlyList<ClickHouseColumn>> DescribeAsync(string sql, CancellationToken cancellationToken)
        => Task.FromResult(_schemas.TryGetValue(sql, out var columns)
            ? columns
            : [new ClickHouseColumn("id", "UInt64", ClickHouseTypeMap.Number), new ClickHouseColumn("name", "String", ClickHouseTypeMap.Text)]);

    public Task<ClickHouseSchema> ReadSchemaAsync(string? table, CancellationToken cancellationToken)
        => Task.FromResult(new ClickHouseSchema(Database, []));

    public Task<ClickHouseConnection> PingAsync(CancellationToken cancellationToken)
        => Task.FromResult(new ClickHouseConnection(Connected, Database, "24.1", ProviderName));
}