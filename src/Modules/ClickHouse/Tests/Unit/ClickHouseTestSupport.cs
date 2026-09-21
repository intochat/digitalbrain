using DigitalBrain.ClickHouse;
using DigitalBrain.ClickHouse.Query;
using DigitalBrain.ClickHouse.Tables;

namespace DigitalBrain.Tests;

// In-memory IClickHouseProvider so the neuron tests never touch a real server.
internal sealed class FakeClickHouseProvider : IClickHouseProvider
{
    private readonly Dictionary<string, IReadOnlyList<ClickHouseColumn>> _schemas = new(StringComparer.Ordinal);

    public string ProviderName => "Fake";
    public bool Connected { get; set; } = true;
    public string Database => "digitalbrain";

    public QueryPlan? LastPlan { get; private set; }

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

    public Task<QueryPage> ExecutePlanAsync(QueryPlan plan, CancellationToken cancellationToken)
    {
        LastPlan = plan;
        IReadOnlyList<ClickHouseTableRow> rows =
        [
            new ClickHouseTableRow("row-0", ["1", "\"alpha\""]),
            new ClickHouseTableRow("row-1", ["2", "\"beta\""]),
        ];
        var total = plan.Filters.Count == 0 ? 2 : 1;
        return Task.FromResult(new QueryPage(rows, total, total));
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