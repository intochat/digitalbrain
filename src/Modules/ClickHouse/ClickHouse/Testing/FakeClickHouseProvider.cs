using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using DigitalBrain.UI;

namespace DigitalBrain.ClickHouse;

// In-memory stand-in selected by DigitalBrainFakes or by an unset provider. It runs the simple
// SELECT shape the tools and scenarios use over seeded tables with ClickHouse's own case-sensitive
// semantics, applies view filters with the same TablePolicy engine as the in-memory table, and
// refuses anything it cannot interpret the way a server refuses bad SQL, unless a test scripted
// that exact SQL.
internal sealed partial class FakeClickHouseProvider : IClickHouseProvider
{
    public const string Name = "Fake";

    private readonly Lock _gate = new();
    private readonly Dictionary<string, FakeTable> _tables = new(StringComparer.Ordinal);
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
            var sampled = 0;
            var tables = _tables.Values
                .Where(entry => table is null || string.Equals(entry.Name, table, StringComparison.Ordinal))
                .OrderBy(entry => entry.Name, StringComparer.Ordinal)
                .Select(entry => new ClickHouseTableInfo(entry.Name, entry.Engine, entry.Rows.Count, entry.Columns
                    .Select((column, index) => ClickHouseTypeMap.IsCategorical(column.ClickHouseType) && sampled++ < ClickHouseSchemaSampling.MaxColumns
                        ? column with { SampleValues = entry.Rows.Select(row => row[index].ToString()).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Take(ClickHouseSchemaSampling.MaxValues).ToArray() }
                        : column)
                    .ToArray()))
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
                throw new ClickHouseQueryException($"The fake ClickHouse provider cannot interpret '{sql}'. Use SELECT columns FROM table [WHERE column op literal AND …] [ORDER BY column] [LIMIT n], or register the result with Script(sql, result). (SYNTAX_ERROR)");
            }

            var tableName = match.Groups["table"].Value;
            if (!_tables.TryGetValue(tableName, out var table))
            {
                throw new ClickHouseQueryException($"Table {Database}.{tableName} does not exist. (UNKNOWN_TABLE)");
            }

            var projection = Projection(table, match.Groups["cols"].Value);
            IEnumerable<IReadOnlyList<JsonElement>> rows = table.Rows;
            if (match.Groups["where"].Success)
            {
                foreach (var predicate in Predicates(table, match.Groups["where"].Value))
                {
                    rows = rows.Where(predicate);
                }
            }

            if (match.Groups["order"].Success)
            {
                var sortColumn = ColumnIndex(table, match.Groups["order"].Value);
                var comparer = Comparer<JsonElement>.Create((left, right) => CompareCells(table.Columns[sortColumn].TableType, left, right));
                rows = match.Groups["dir"].Value.Equals("DESC", StringComparison.OrdinalIgnoreCase)
                    ? rows.OrderByDescending(row => row[sortColumn], comparer)
                    : rows.OrderBy(row => row[sortColumn], comparer);
            }

            if (match.Groups["limit"].Success)
            {
                rows = rows.Take(int.Parse(match.Groups["limit"].Value, CultureInfo.InvariantCulture));
            }

            var projected = rows.Select((row, index) => new TableRow($"row-{index}", projection.Select(column => row[column]).ToArray())).ToArray();
            return (projection.Select(column => table.Columns[column]).ToArray(), projected);
        }
    }

    private static int[] Projection(FakeTable table, string columns)
        => columns.Trim() == "*"
            ? Enumerable.Range(0, table.Columns.Count).ToArray()
            : columns.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(name => ColumnIndex(table, name)).ToArray();

    private static int ColumnIndex(FakeTable table, string name)
    {
        for (var index = 0; index < table.Columns.Count; index++)
        {
            if (string.Equals(table.Columns[index].Name, name, StringComparison.Ordinal))
            {
                return index;
            }
        }

        throw new ClickHouseQueryException($"Missing columns: '{name}' while processing query. (UNKNOWN_IDENTIFIER)");
    }

    // Raw SQL predicates compare the way ClickHouse does: case-sensitive strings, numeric numbers.
    private static IEnumerable<Func<IReadOnlyList<JsonElement>, bool>> Predicates(FakeTable table, string where)
    {
        foreach (var clause in AndSeparator().Split(where))
        {
            var predicate = SimplePredicate().Match(clause.Trim());
            if (!predicate.Success)
            {
                throw new ClickHouseQueryException($"The fake ClickHouse provider cannot interpret the predicate '{clause.Trim()}'. Use column op literal, or register the result with Script(sql, result). (SYNTAX_ERROR)");
            }

            var column = ColumnIndex(table, predicate.Groups["col"].Value);
            var operand = Literal(table.Columns[column].TableType, predicate.Groups["val"].Value);
            var @operator = predicate.Groups["op"].Value;
            yield return row =>
            {
                var cell = row[column];
                if (cell.ValueKind == JsonValueKind.Null)
                {
                    return false;
                }

                var comparison = CompareCells(table.Columns[column].TableType, cell, operand);
                return @operator switch
                {
                    "=" => comparison == 0,
                    "!=" or "<>" => comparison != 0,
                    ">" => comparison > 0,
                    ">=" => comparison >= 0,
                    "<" => comparison < 0,
                    _ => comparison <= 0,
                };
            };
        }
    }

    private static int CompareCells(string tableType, JsonElement left, JsonElement right)
    {
        if (left.ValueKind == JsonValueKind.Null) { return right.ValueKind == JsonValueKind.Null ? 0 : -1; }
        if (right.ValueKind == JsonValueKind.Null) { return 1; }
        return tableType switch
        {
            ClickHouseTypeMap.Number => left.GetDecimal().CompareTo(right.GetDecimal()),
            ClickHouseTypeMap.Boolean => left.GetBoolean().CompareTo(right.GetBoolean()),
            _ => string.CompareOrdinal(left.GetString(), right.GetString()),
        };
    }

    // The literal takes the column's type the way ClickHouse would coerce it (is_active = 1, count = '50'),
    // and refuses what the server refuses instead of leaking a conversion exception.
    private static JsonElement Literal(string tableType, string literal)
    {
        var text = literal.StartsWith('\'') ? literal[1..^1].Replace("''", "'", StringComparison.Ordinal) : literal;
        return tableType switch
        {
            ClickHouseTypeMap.Number => decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var number)
                ? JsonSerializer.SerializeToElement(number)
                : throw new ClickHouseQueryException($"Cannot parse '{text}' as a number. (CANNOT_PARSE_TEXT)"),
            ClickHouseTypeMap.Boolean => text is "1" or "0" || bool.TryParse(text, out _)
                ? JsonSerializer.SerializeToElement(text == "1" || (bool.TryParse(text, out var flag) && flag))
                : throw new ClickHouseQueryException($"Cannot parse '{text}' as Bool. (CANNOT_PARSE_BOOL)"),
            _ => JsonSerializer.SerializeToElement(text),
        };
    }

    private static string Normalize(string sql) => Whitespace().Replace(sql.Trim().TrimEnd(';'), " ");

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"\s+AND\s+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AndSeparator();

    [GeneratedRegex(@"\A\s*SELECT\s+(?<cols>\*|[A-Za-z_][A-Za-z0-9_]*(?:\s*,\s*[A-Za-z_][A-Za-z0-9_]*)*)\s+FROM\s+(?:[A-Za-z_][A-Za-z0-9_]*\.)?(?<table>[A-Za-z_][A-Za-z0-9_]*)(?:\s+WHERE\s+(?<where>.+?))?(?:\s+ORDER\s+BY\s+(?<order>[A-Za-z_][A-Za-z0-9_]*)(?:\s+(?<dir>ASC|DESC))?)?(?:\s+LIMIT\s+(?<limit>\d+))?\s*\z",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex SimpleSelect();

    [GeneratedRegex(@"\A(?<col>[A-Za-z_][A-Za-z0-9_]*)\s*(?<op>>=|<=|!=|<>|=|>|<)\s*(?<val>'(?:[^']|'')*'|-?\d+(?:\.\d+)?|true|false)\z",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SimplePredicate();

    private sealed record FakeTable(string Name, string Engine, IReadOnlyList<ClickHouseColumn> Columns, IReadOnlyList<IReadOnlyList<JsonElement>> Rows);
}
