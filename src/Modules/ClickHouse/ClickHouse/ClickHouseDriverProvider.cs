using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using ClickHouse.Driver;
using ClickHouse.Driver.ADO;
using ClickHouse.Driver.ADO.Parameters;
using DigitalBrain.UI;

namespace DigitalBrain.ClickHouse;

// Every statement goes through ExecuteReaderAsync with the same server-side caps: readonly=2,
// a bounded result, a bounded scan, a memory ceiling and a 15 second execution limit.
internal sealed class ClickHouseDriverProvider(ClickHouseClient client, string database) : IClickHouseProvider
{
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan PingTimeout = TimeSpan.FromSeconds(3);
    private const long MaxRowsToRead = 50_000_000;
    private const long MaxMemoryBytes = 2_000_000_000;

    public string ProviderName => ClickHouseModule.DriverProviderName;

    public async Task<ClickHouseQueryResult> QueryAsync(string sql, int maxRows, CancellationToken cancellationToken)
    {
        ClickHouseQueryGuard.Validate(sql);
        var started = Stopwatch.GetTimestamp();
        var parameters = new ClickHouseParameterCollection();
        parameters.Add(Parameter("limit", (ulong)maxRows + 1));
        // One row past the cap tells truncation apart from a result that exactly fills it.
        var (columns, rows) = await ReadAsync($"SELECT * FROM ({sql}) AS q LIMIT {{limit:UInt64}}", parameters, maxRows + 1, cancellationToken).ConfigureAwait(false);
        var truncated = rows.Count > maxRows;
        var page = truncated ? rows.Take(maxRows).ToArray() : rows.ToArray();
        return new(columns, page, page.Length, truncated, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
    }

    public async Task<QueryPage> ExecutePlanAsync(QueryPlan plan, CancellationToken cancellationToken)
    {
        ClickHouseQueryGuard.Validate(plan.BaseSql);
        var compiled = QueryPlanCompiler.Compile(plan);
        var parameters = new ClickHouseParameterCollection();
        foreach (var (name, value) in compiled.Parameters)
        {
            parameters.Add(Parameter(name, value));
        }

        var (_, rows) = await ReadAsync(compiled.PageSql, parameters, plan.Limit, cancellationToken).ConfigureAwait(false);
        var filtered = await CountAsync(compiled.FilteredCountSql, parameters, cancellationToken).ConfigureAwait(false);
        var total = plan.Filters.Count == 0 ? filtered : await CountAsync(compiled.TotalCountSql, parameters, cancellationToken).ConfigureAwait(false);
        var page = rows.Select((cells, index) => new TableRow($"row-{plan.Offset + index}", cells)).ToArray();
        return new(page, total, filtered);
    }

    public async Task<IReadOnlyList<ClickHouseColumn>> DescribeAsync(string sql, CancellationToken cancellationToken)
    {
        ClickHouseQueryGuard.Validate(sql);
        var (columns, _) = await ReadAsync($"SELECT * FROM ({sql}) AS q LIMIT 0", null, 1, cancellationToken).ConfigureAwait(false);
        return columns;
    }

    public async Task<ClickHouseSchema> ReadSchemaAsync(string? table, CancellationToken cancellationToken)
    {
        var parameters = new ClickHouseParameterCollection();
        parameters.Add(Parameter("db", database));
        var tableNameFilter = string.Empty;
        var tableColumnFilter = string.Empty;
        if (table is not null)
        {
            parameters.Add(Parameter("t", table));
            tableNameFilter = " AND name = {t:String}";
            tableColumnFilter = " AND table = {t:String}";
        }

        // Both reads are pinned to the configured database, so system.* and other databases stay invisible.
        var (_, tableRows) = await ReadAsync(
            "SELECT name, engine, total_rows FROM system.tables WHERE database = {db:String}" + tableNameFilter + " ORDER BY name",
            parameters, 10_000, cancellationToken).ConfigureAwait(false);
        var (_, columnRows) = await ReadAsync(
            "SELECT table, name, type FROM system.columns WHERE database = {db:String}" + tableColumnFilter + " ORDER BY table, position",
            parameters, 100_000, cancellationToken).ConfigureAwait(false);
        var columnsByTable = columnRows
            .GroupBy(row => row[0].GetString()!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<ClickHouseColumn>)group
                .Select(row => new ClickHouseColumn(row[1].GetString()!, row[2].GetString()!, ClickHouseTypeMap.ToTableType(row[2].GetString()!)))
                .ToArray(), StringComparer.Ordinal);
        var tables = tableRows.Select(row => new ClickHouseTableInfo(
            row[0].GetString()!,
            row[1].GetString()!,
            row[2].ValueKind == JsonValueKind.Number ? row[2].GetInt64() : null,
            columnsByTable.GetValueOrDefault(row[0].GetString()!) ?? [])).ToArray();
        return new(database, tables);
    }

    public async Task<ClickHouseConnection> PingAsync(CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(PingTimeout);
        try
        {
            var (_, rows) = await ReadAsync("SELECT version()", null, 1, deadline.Token).ConfigureAwait(false);
            return new(true, database, rows.Count == 1 ? rows[0][0].GetString() : null, ProviderName);
        }
        catch (Exception error) when (error is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return new(false, database, null, ProviderName);
        }
    }

    private async Task<long> CountAsync(string sql, ClickHouseParameterCollection parameters, CancellationToken cancellationToken)
    {
        var (_, rows) = await ReadAsync(sql, parameters, 1, cancellationToken).ConfigureAwait(false);
        return rows.Count == 1 && rows[0][0].ValueKind == JsonValueKind.Number ? rows[0][0].GetInt64() : long.Parse(rows[0][0].GetString()!, CultureInfo.InvariantCulture);
    }

    private async Task<(ClickHouseColumn[] Columns, List<JsonElement[]> Rows)> ReadAsync(
        string sql, ClickHouseParameterCollection? parameters, int maxResultRows, CancellationToken cancellationToken)
    {
        try
        {
            using var reader = await client.ExecuteReaderAsync(sql, parameters, Options(maxResultRows), cancellationToken).ConfigureAwait(false);
            var columns = new ClickHouseColumn[reader.FieldCount];
            for (var index = 0; index < columns.Length; index++)
            {
                var type = reader.GetDataTypeName(index);
                columns[index] = new(reader.GetName(index), type, ClickHouseTypeMap.ToTableType(type));
            }

            var rows = new List<JsonElement[]>();
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var cells = new JsonElement[columns.Length];
                for (var index = 0; index < cells.Length; index++)
                {
                    cells[index] = ClickHouseCells.ToCell(reader.IsDBNull(index) ? null : reader.GetValue(index), columns[index].TableType);
                }

                rows.Add(cells);
            }

            return (columns, rows);
        }
        catch (ClickHouseServerException error)
        {
            throw new ClickHouseQueryException(error.Message);
        }
        catch (HttpRequestException error)
        {
            throw new ClickHouseUnavailableException($"ClickHouse is unreachable: {error.Message}");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ClickHouseQueryException($"The query did not finish within {QueryTimeout.TotalSeconds:0} seconds. Narrow it with WHERE or LIMIT.");
        }
    }

    private static ClickHouseDbParameter Parameter(string name, object value) => new() { ParameterName = name, Value = value };

    private static QueryOptions Options(int maxResultRows) => new()
    {
        QueryId = $"digitalbrain-{Guid.NewGuid():N}",
        MaxExecutionTime = QueryTimeout,
        CustomSettings = new Dictionary<string, object>
        {
            ["readonly"] = 2,
            ["max_result_rows"] = maxResultRows,
            ["result_overflow_mode"] = "break",
            ["max_rows_to_read"] = MaxRowsToRead,
            ["max_memory_usage"] = MaxMemoryBytes,
        },
    };

    internal static string DatabaseOf(string connectionString)
    {
        var settings = new ClickHouseConnectionStringBuilder(connectionString);
        return string.IsNullOrWhiteSpace(settings.Database) ? "default" : settings.Database;
    }
}
