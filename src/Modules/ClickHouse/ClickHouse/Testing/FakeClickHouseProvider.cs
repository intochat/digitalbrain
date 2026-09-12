using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using DigitalBrain.UI;

namespace DigitalBrain.ClickHouse;

// In-memory stand-in selected by DigitalBrainFakes or by an unset provider. It runs the simple
// SELECT shape the tools and scenarios use over seeded tables, applies view filters with the
// same TablePolicy engine as the in-memory table, and refuses anything it cannot interpret
// unless a test scripted that exact SQL.
internal sealed partial class FakeClickHouseProvider : IClickHouseProvider
{
    public const string Name = "Fake";

    private readonly Lock _gate = new();
    private readonly Dictionary<string, FakeTable> _tables = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ClickHouseQueryResult> _scripted = new(StringComparer.Ordinal);
    private Exception? _failNext;

    public FakeClickHouseProvider() => FakeLeads.Seed(this);

    public string ProviderName => Name;

    public string Database => ClickHouseNames.DatabaseName;

    public void RegisterTable(string name, IReadOnlyList<ClickHouseColumn> columns, IReadOnlyList<IReadOnlyList<JsonElement>> rows, string engine = "MergeTree")
    {
        lock (_gate)
        {
            _tables[name] = new(name, engine, columns, rows);
        }
    }

    public void Script(string sql, ClickHouseQueryResult result)
    {
        lock (_gate)
        {
            _scripted[Normalize(sql)] = result;
        }
    }

    public void FailNext(Exception error)
    {
        lock (_gate)
        {
            _failNext = error;
        }
    }

    public Task<ClickHouseQueryResult> QueryAsync(string sql, int maxRows, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowPending();
        ClickHouseQueryGuard.Validate(sql);
        var (columns, rows) = Resolve(sql);
        var truncated = rows.Count > maxRows;
        var page = rows.Take(maxRows).Select(row => row.Cells).ToArray();
        return Task.FromResult(new ClickHouseQueryResult(columns, page, page.Length, truncated, 0.1));
    }

    public Task<QueryPage> ExecutePlanAsync(QueryPlan plan, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowPending();
        ClickHouseQueryGuard.Validate(plan.BaseSql);
        var (columns, rows) = Resolve(plan.BaseSql);
        var tableColumns = columns.Select(column => new TableColumn(column.Name, column.Name, column.TableType)).ToArray();
        var source = new TableSnapshot("fake", "fake", 1, tableColumns, rows, plan.Filters, plan.Sort,
            tableColumns.Select(column => column.Id).ToArray(), rows.Count, rows.Count, 0, 50);
        var filtered = TablePolicy.Apply(source);
        var page = filtered.Skip(plan.Offset).Take(plan.Limit)
            .Select((row, index) => new TableRow($"row-{plan.Offset + index}", row.Cells)).ToArray();
        return Task.FromResult(new QueryPage(page, rows.Count, filtered.Count));
    }

    public Task<IReadOnlyList<ClickHouseColumn>> DescribeAsync(string sql, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowPending();
        ClickHouseQueryGuard.Validate(sql);
        return Task.FromResult(Resolve(sql).Columns);
    }

    public Task<ClickHouseSchema> ReadSchemaAsync(string? table, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowPending();
        lock (_gate)
        {
            var tables = _tables.Values
                .Where(entry => table is null || string.Equals(entry.Name, table, StringComparison.OrdinalIgnoreCase))
                .OrderBy(entry => entry.Name, StringComparer.Ordinal)
                .Select(entry => new ClickHouseTableInfo(entry.Name, entry.Engine, entry.Rows.Count, entry.Columns))
                .ToArray();
            return Task.FromResult(new ClickHouseSchema(Database, tables));
        }
    }

    public Task<ClickHouseConnection> PingAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new ClickHouseConnection(true, Database, "fake", Name));
    }

    private void ThrowPending()
    {
        Exception? pending;
        lock (_gate)
        {
            pending = _failNext;
            _failNext = null;
        }

        if (pending is not null)
        {
            throw pending;
        }
    }

    private (IReadOnlyList<ClickHouseColumn> Columns, IReadOnlyList<TableRow> Rows) Resolve(string sql)
    {
        lock (_gate)
        {
            if (_scripted.TryGetValue(Normalize(sql), out var scripted))
            {
                return (scripted.Columns, scripted.Rows.Select((cells, index) => new TableRow($"row-{index}", cells)).ToArray());
            }

            var match = SimpleSelect().Match(sql);
            if (!match.Success)
            {
                throw new NotSupportedException($"The fake ClickHouse provider cannot interpret '{sql}'. Register its result with Script(sql, result).");
            }

            var tableName = match.Groups["table"].Value;
            if (!_tables.TryGetValue(tableName, out var table))
            {
                throw new ClickHouseQueryException($"Table {Database}.{tableName} does not exist. (UNKNOWN_TABLE)");
            }

            var projection = Projection(table, match.Groups["cols"].Value);
            var tableColumns = table.Columns.Select(column => new TableColumn(column.Name, column.Name, column.TableType)).ToArray();
            var filters = match.Groups["where"].Success ? Filters(table, match.Groups["where"].Value) : [];
            TableSort? sort = match.Groups["order"].Success
                ? new(RequireColumn(table, match.Groups["order"].Value).Name, match.Groups["dir"].Value.Equals("DESC", StringComparison.OrdinalIgnoreCase))
                : null;
            var source = new TableSnapshot("fake", "fake", 1, tableColumns,
                table.Rows.Select((cells, index) => new TableRow($"row-{index}", cells)).ToArray(),
                filters, sort, tableColumns.Select(column => column.Id).ToArray(), table.Rows.Count, table.Rows.Count, 0, 50);
            IEnumerable<TableRow> rows = TablePolicy.Apply(source);
            if (match.Groups["limit"].Success)
            {
                rows = rows.Take(int.Parse(match.Groups["limit"].Value, CultureInfo.InvariantCulture));
            }

            var projected = rows.Select(row => new TableRow(row.Id, projection.Select(index => row.Cells[index]).ToArray())).ToArray();
            return (projection.Select(index => table.Columns[index]).ToArray(), projected);
        }
    }

    private static int[] Projection(FakeTable table, string columns)
    {
        if (columns.Trim() == "*")
        {
            return Enumerable.Range(0, table.Columns.Count).ToArray();
        }

        return columns.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(name => table.Columns.ToList().FindIndex(column => string.Equals(column.Name, RequireColumn(table, name).Name, StringComparison.Ordinal)))
            .ToArray();
    }

    private static ClickHouseColumn RequireColumn(FakeTable table, string name)
        => table.Columns.FirstOrDefault(column => string.Equals(column.Name, name, StringComparison.Ordinal))
            ?? throw new ClickHouseQueryException($"Missing columns: '{name}' while processing query. (UNKNOWN_IDENTIFIER)");

    private static TableFilter[] Filters(FakeTable table, string where)
        => where.Split(" AND ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Concat(where.Contains(" and ", StringComparison.Ordinal) ? where.Split(" and ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) : [])
            .Where(clause => !clause.Contains(" AND ", StringComparison.Ordinal) && !clause.Contains(" and ", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .Select(clause =>
            {
                var predicate = SimplePredicate().Match(clause);
                if (!predicate.Success)
                {
                    throw new NotSupportedException($"The fake ClickHouse provider cannot interpret the predicate '{clause}'. Register the result with Script(sql, result).");
                }

                var column = RequireColumn(table, predicate.Groups["col"].Value);
                var @operator = predicate.Groups["op"].Value switch
                {
                    "=" => "eq",
                    "!=" or "<>" => "neq",
                    ">" => "gt",
                    ">=" => "gte",
                    "<" => "lt",
                    _ => "lte",
                };
                return new TableFilter(column.Name, @operator, Literal(column.TableType, predicate.Groups["val"].Value));
            })
            .ToArray();

    private static JsonElement Literal(string tableType, string literal)
    {
        if (literal.StartsWith('\''))
        {
            return JsonSerializer.SerializeToElement(literal[1..^1].Replace("''", "'", StringComparison.Ordinal));
        }

        if (bool.TryParse(literal, out var flag))
        {
            return JsonSerializer.SerializeToElement(flag);
        }

        var number = decimal.Parse(literal, CultureInfo.InvariantCulture);
        return tableType == ClickHouseTypeMap.Number
            ? JsonSerializer.SerializeToElement(number)
            : JsonSerializer.SerializeToElement(literal);
    }

    private static string Normalize(string sql) => Whitespace().Replace(sql.Trim().TrimEnd(';'), " ");

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"\A\s*SELECT\s+(?<cols>\*|[A-Za-z_][A-Za-z0-9_]*(?:\s*,\s*[A-Za-z_][A-Za-z0-9_]*)*)\s+FROM\s+(?:[A-Za-z_][A-Za-z0-9_]*\.)?(?<table>[A-Za-z_][A-Za-z0-9_]*)(?:\s+WHERE\s+(?<where>.+?))?(?:\s+ORDER\s+BY\s+(?<order>[A-Za-z_][A-Za-z0-9_]*)(?:\s+(?<dir>ASC|DESC))?)?(?:\s+LIMIT\s+(?<limit>\d+))?\s*\z",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex SimpleSelect();

    [GeneratedRegex(@"\A(?<col>[A-Za-z_][A-Za-z0-9_]*)\s*(?<op>>=|<=|!=|<>|=|>|<)\s*(?<val>'(?:[^']|'')*'|-?\d+(?:\.\d+)?|true|false)\z",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SimplePredicate();

    private sealed record FakeTable(string Name, string Engine, IReadOnlyList<ClickHouseColumn> Columns, IReadOnlyList<IReadOnlyList<JsonElement>> Rows);
}
