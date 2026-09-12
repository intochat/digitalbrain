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
        var parameters = new ClickHouseParameterCollection { Parameter("limit", (ulong)maxRows + 1) };
        // One row past the cap tells truncation apart from a result that exactly fills it.
        var (columns, rows) = await ReadAsync($"SELECT * FROM ({sql}) AS q LIMIT {{limit:UInt64}}", parameters, maxRows + 1, cancellationToken).ConfigureAwait(false);
        var truncated = rows.Count > maxRows;
        var page = truncated ? rows.Take(maxRows).ToArray() : rows.ToArray();
        return new(columns, page, page.Length, truncated, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
    }

    // The page and its counts are independent statements, so they run concurrently and the
    // slowest one bounds the latency instead of their sum.
    public async Task<QueryPage> ExecutePlanAsync(QueryPlan plan, CancellationToken cancellationToken)
    {
        ClickHouseQueryGuard.Validate(plan.BaseSql);
        var compiled = QueryPlanCompiler.Compile(plan);
        var pageRead = ReadAsync(compiled.PageSql, Parameters(compiled.Parameters), plan.Limit, cancellationToken);
        var filteredCount = CountAsync(compiled.FilteredCountSql, Parameters(compiled.Parameters), cancellationToken);
        var totalCount = plan.Filters.Count == 0 ? filteredCount : CountAsync(compiled.TotalCountSql, Parameters(compiled.Parameters), cancellationToken);
        await Task.WhenAll(pageRead, filteredCount, totalCount).ConfigureAwait(false);
        var page = pageRead.Result.Rows.Select((cells, index) => new TableRow($"row-{plan.Offset + index}", cells)).ToArray();
        return new(page, totalCount.Result, filteredCount.Result);
    }

    public async Task<IReadOnlyList<ClickHouseColumn>> DescribeAsync(string sql, CancellationToken cancellationToken)
    {
        ClickHouseQueryGuard.Validate(sql);
        var (columns, _) = await ReadAsync($"SELECT * FROM ({sql}) AS q LIMIT 0", null, 1, cancellationToken).ConfigureAwait(false);
        return columns;
    }

    public async Task<ClickHouseSchema> ReadSchemaAsync(string? table, CancellationToken cancellationToken)
    {
        var tableNameFilter = string.Empty;
        var tableColumnFilter = string.Empty;
        if (table is not null)
        {
            tableNameFilter = " AND name = {t:String}";
            tableColumnFilter = " AND table = {t:String}";
        }

        // Both reads are pinned to the configured database, so system.* and other databases stay invisible.
        var (_, tableRows) = await ReadAsync(
            "SELECT name, engine, total_rows FROM system.tables WHERE database = {db:String}" + tableNameFilter + " ORDER BY name",
            SchemaParameters(table), 10_000, cancellationToken).ConfigureAwait(false);
        var (_, columnRows) = await ReadAsync(
            "SELECT table, name, type FROM system.columns WHERE database = {db:String}" + tableColumnFilter + " ORDER BY table, position",
            SchemaParameters(table), 100_000, cancellationToken).ConfigureAwait(false);

        var sampled = 0;
        var columnsByTable = new Dictionary<string, List<(ClickHouseColumn Column, Task<IReadOnlyList<string>?>? Samples)>>(StringComparer.Ordinal);
        foreach (var row in columnRows)
        {
            var tableName = row[0].GetString()!;
            var name = row[1].GetString()!;
            var type = row[2].GetString()!;
            Task<IReadOnlyList<string>?>? samples = null;
            if (ClickHouseTypeMap.IsCategorical(type) && sampled < ClickHouseSchemaSampling.MaxColumns)
            {
                sampled++;
                samples = SampleValuesAsync(tableName, name, cancellationToken);
            }

            if (!columnsByTable.TryGetValue(tableName, out var columns))
            {
                columnsByTable[tableName] = columns = [];
            }

            columns.Add((new(name, type, ClickHouseTypeMap.ToTableType(type)), samples));
        }

        await Task.WhenAll(columnsByTable.Values.SelectMany(columns => columns).Select(entry => entry.Samples).OfType<Task>()).ConfigureAwait(false);
        var tables = tableRows.Select(row =>
        {
            var name = row[0].GetString()!;
            var columns = columnsByTable.GetValueOrDefault(name) ?? [];
            return new ClickHouseTableInfo(
                name,
                row[1].GetString()!,
                row[2].ValueKind == JsonValueKind.Number ? row[2].GetInt64() : null,
                columns.Select(entry => entry.Samples is { } samples ? entry.Column with { SampleValues = samples.Result } : entry.Column).ToArray());
        }).ToArray();
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

    // A handful of real values for a categorical column, so the agent writes country = 'GB', not
    // 'UK'. A column that cannot be sampled within the caps simply has no samples; it never fails
    // the schema read.
    private async Task<IReadOnlyList<string>?> SampleValuesAsync(string table, string column, CancellationToken cancellationToken)
    {
        var parameters = new ClickHouseParameterCollection
        {
            Parameter("db", database),
            Parameter("t", table),
            Parameter("c", column),
            Parameter("n", (ulong)ClickHouseSchemaSampling.MaxValues),
        };
        try
        {
            var (_, rows) = await ReadAsync(
                "SELECT DISTINCT toString({c:Identifier}) AS v FROM {db:Identifier}.{t:Identifier} ORDER BY v LIMIT {n:UInt64}",
                parameters, ClickHouseSchemaSampling.MaxValues, cancellationToken).ConfigureAwait(false);
            return rows.Select(row => row[0].GetString() ?? string.Empty).ToArray();
        }
        catch (ClickHouseQueryException)
        {
            return null;
        }
    }

    private ClickHouseParameterCollection SchemaParameters(string? table)
    {
        var parameters = new ClickHouseParameterCollection { Parameter("db", database) };
        if (table is not null)
        {
            parameters.Add(Parameter("t", table));
        }

        return parameters;
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

    // Each concurrent statement gets its own collection; the driver reads it while building the request.
    private static ClickHouseParameterCollection Parameters(IReadOnlyDictionary<string, object> values)
    {
        var parameters = new ClickHouseParameterCollection();
        foreach (var (name, value) in values)
        {
            parameters.Add(Parameter(name, value));
        }

        return parameters;
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

// Schema reads sample this many categorical columns with this many distinct values each; the fake
// applies the same caps so scenarios see what the driver would return.
internal static class ClickHouseSchemaSampling
{
    public const int MaxColumns = 24;
    public const int MaxValues = 12;
}
