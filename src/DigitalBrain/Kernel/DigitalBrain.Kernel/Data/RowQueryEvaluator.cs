using System.Globalization;
using DigitalBrain.Contracts.Data;

namespace DigitalBrain.Kernel.Data;

internal static class RowQueryEvaluator
{
    public static RowPage Evaluate(RowSchema schema, Row[] rows, RowQuery query)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(query);
        query = query.Degrade(new SourceCapabilities());
        query.Check(schema);

        var filtered = rows.Where(row => query.Filters.All(filter => Match(schema, row, filter))).ToArray();
        var grouped = query.GroupBy.Length > 0 || query.Aggregates.Length > 0;
        RowColumn[] columns;
        Row[] projected;
        if (!grouped)
        {
            var names = query.Columns.Length == 0 ? schema.Columns.Select(column => column.Name).ToArray() : query.Columns;
            if (names.Length == 0) { throw new ArgumentException("A query needs a column."); }
            columns = names.Select(name => Column(schema, name)).ToArray();
            projected = filtered.Select(row => new Row(names.Select(name => Cell(schema, row, name)).ToArray())).ToArray();
        }
        else
        {
            if (query.GroupBy.Length == 0 && query.Aggregates.Length == 0)
            {
                throw new ArgumentException("A grouped query needs a column.");
            }

            Row[][] groups = query.GroupBy.Length == 0
                ? [filtered]
                : filtered.GroupBy(row => System.Text.Json.JsonSerializer.Serialize(query.GroupBy.Select(name => Cell(schema, row, name)))).Select(group => group.ToArray()).ToArray();
            columns = [.. query.GroupBy.Select(name => Column(schema, name)), .. query.Aggregates.Select(aggregate => new RowColumn(Name(aggregate), aggregate.Function.Trim().ToLowerInvariant() is "min" or "max" ? Column(schema, aggregate.Column!).Type : "number"))];
            projected = groups.Select(group =>
            {
                var values = query.GroupBy.Select(name => Cell(schema, group.First(), name)).ToList();
                foreach (var aggregate in query.Aggregates) { values.Add(Aggregate(schema, group.ToArray(), aggregate)); }
                return new Row([.. values]);
            }).ToArray();
        }

        if (columns.Select(column => column.Name).Distinct(StringComparer.Ordinal).Count() != columns.Length)
        {
            throw new ArgumentException("A result column is named once.");
        }

        projected = Sort(columns, projected, query.Sort);
        var page = projected.Skip(query.Offset).Take(query.Limit).ToArray();
        return new RowPage(columns, page, query.Offset + page.Length < projected.Length);
    }

    private static bool Match(RowSchema schema, Row row, RowFilter filter)
    {
        var cell = Cell(schema, row, filter.Column);
        var op = filter.Operator.Trim().ToLowerInvariant();
        if (op == "contains") { return cell.Contains(filter.Value, StringComparison.Ordinal); }
        var compared = Compare(cell, filter.Value, Column(schema, filter.Column).Type);
        return op switch
        {
            "eq" => compared == 0,
            "ne" => compared != 0,
            "gt" => compared > 0,
            "gte" => compared >= 0,
            "lt" => compared < 0,
            "lte" => compared <= 0,
            _ => throw new ArgumentException($"Operator '{filter.Operator}' is not supported."),
        };
    }

    private static int Compare(string left, string right, string type)
    {
        if (type == "number" && double.TryParse(left, NumberStyles.Float, CultureInfo.InvariantCulture, out var leftNumber)
            && double.TryParse(right, NumberStyles.Float, CultureInfo.InvariantCulture, out var rightNumber)
            && double.IsFinite(leftNumber) && double.IsFinite(rightNumber))
        {
            return leftNumber.CompareTo(rightNumber);
        }

        return string.Compare(left, right, StringComparison.Ordinal);
    }

    private static string Aggregate(RowSchema schema, Row[] rows, RowAggregate aggregate)
    {
        var function = aggregate.Function.Trim().ToLowerInvariant();
        if (function == "count" && string.IsNullOrWhiteSpace(aggregate.Column))
        {
            return rows.Length.ToString(CultureInfo.InvariantCulture);
        }

        var values = rows.Select(row => Cell(schema, row, aggregate.Column ?? "")).ToArray();
        if (function == "count")
        {
            return values.Length.ToString(CultureInfo.InvariantCulture);
        }

        var numbers = new List<double>();
        foreach (var value in values)
        {
            if (value.Length == 0) { continue; }
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number))
            {
                numbers.Add(number);
            }
        }

        if (function is "min" or "max" && Column(schema, aggregate.Column!).Type != "number")
        {
            var text = values.Where(value => value.Length > 0).ToArray();
            if (text.Length == 0) { return ""; }
            return function == "min" ? text.Min(StringComparer.Ordinal)! : text.Max(StringComparer.Ordinal)!;
        }

        if (numbers.Count == 0) { return ""; }
        var result = function switch
        {
            "sum" => numbers.Sum(),
            "avg" => numbers.Average(),
            "min" => numbers.Min(),
            "max" => numbers.Max(),
            _ => throw new ArgumentException($"Aggregate '{aggregate.Function}' is not supported."),
        };
        return Format(result);
    }

    private static Row[] Sort(RowColumn[] columns, Row[] rows, RowSort[] sorts)
    {
        IOrderedEnumerable<Row>? ordered = null;
        foreach (var sort in sorts)
        {
            var index = Array.FindIndex(columns, column => column.Name == sort.Column);
            if (index < 0) { throw new ArgumentException($"Sort column '{sort.Column}' is not in the result."); }
            var comparer = Comparer<string>.Create((left, right) => Compare(left, right, columns[index].Type));
            ordered = ordered is null
                ? sort.Descending
                    ? rows.OrderByDescending(row => row.Values[index], comparer)
                    : rows.OrderBy(row => row.Values[index], comparer)
                : sort.Descending
                    ? ordered.ThenByDescending(row => row.Values[index], comparer)
                    : ordered.ThenBy(row => row.Values[index], comparer);
        }

        return ordered?.ToArray() ?? rows;
    }

    private static RowColumn Column(RowSchema schema, string name)
    {
        var column = schema.Columns.FirstOrDefault(candidate => candidate.Name == name)
            ?? throw new ArgumentException($"Column '{name}' is not in the schema.");
        return column;
    }

    private static string Cell(RowSchema schema, Row row, string name)
    {
        var index = Array.FindIndex(schema.Columns, column => column.Name == name);
        if (index < 0) { throw new ArgumentException($"Column '{name}' is not in the schema."); }
        return index < row.Values.Length ? row.Values[index] : "";
    }

    private static string Name(RowAggregate aggregate)
        => string.IsNullOrWhiteSpace(aggregate.Alias) ? aggregate.Function.Trim().ToLowerInvariant() : aggregate.Alias;

    private static string Format(double value)
    {
        if (double.IsFinite(value) && value == Math.Truncate(value) && Math.Abs(value) < 1e15)
        {
            return ((long)value).ToString(CultureInfo.InvariantCulture);
        }

        return value.ToString("G15", CultureInfo.InvariantCulture);
    }
}
