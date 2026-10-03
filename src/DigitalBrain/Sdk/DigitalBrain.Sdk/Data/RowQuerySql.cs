using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using DigitalBrain.Contracts.Data;

namespace DigitalBrain.Sdk.Data;

// RowQuery compiled to one guard-safe SELECT: quoted identifiers, single-quoted literals, no parameters.
public static class RowQuerySql
{
    private static readonly Regex Identifier = new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    public static string Compile(string table, RowSchema schema, RowQuery query)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(table);
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(query);
        query = query.Degrade(new SourceCapabilities());
        query.Check(schema);

        var grouped = query.GroupBy.Length > 0 || query.Aggregates.Length > 0;
        var select = new List<string>();
        var output = new List<string>();
        if (grouped)
        {
            foreach (var column in query.GroupBy)
            {
                select.Add(Quote(Require(schema, column).Name));
                output.Add(column);
            }

            foreach (var aggregate in query.Aggregates)
            {
                select.Add(Aggregate(schema, aggregate));
                output.Add(Name(aggregate));
            }
        }
        else
        {
            var names = query.Columns.Length == 0 ? schema.Columns.Select(column => column.Name).ToArray() : query.Columns;
            if (names.Length == 0) { throw new ArgumentException("A query needs a column."); }
            foreach (var name in names)
            {
                select.Add(Quote(Require(schema, name).Name));
                output.Add(name);
            }
        }

        if (select.Count == 0) { throw new ArgumentException("A query needs a column."); }
        if (output.Distinct(StringComparer.Ordinal).Count() != output.Count)
        {
            throw new ArgumentException("A result column is named once.");
        }

        var sql = new StringBuilder();
        sql.Append("SELECT ").Append(string.Join(", ", select));
        sql.Append(" FROM ").Append(QuoteTable(table));
        if (query.Filters.Length > 0)
        {
            sql.Append(" WHERE ");
            sql.Append(string.Join(" AND ", query.Filters.Select(filter => Predicate(schema, filter))));
        }

        if (query.GroupBy.Length > 0)
        {
            sql.Append(" GROUP BY ");
            sql.Append(string.Join(", ", query.GroupBy.Select(column => Quote(Require(schema, column).Name))));
        }

        if (query.Sort.Length > 0)
        {
            sql.Append(" ORDER BY ");
            sql.Append(string.Join(", ", query.Sort.Select(sort =>
            {
                if (!output.Contains(sort.Column, StringComparer.Ordinal))
                {
                    throw new ArgumentException($"Sort column '{sort.Column}' is not in the result.");
                }

                return Quote(sort.Column) + (sort.Descending ? " DESC" : " ASC");
            })));
        }

        sql.Append(" LIMIT ").Append(query.Limit.ToString(CultureInfo.InvariantCulture));
        sql.Append(" OFFSET ").Append(query.Offset.ToString(CultureInfo.InvariantCulture));
        return sql.ToString();
    }

    private static string Aggregate(RowSchema schema, RowAggregate aggregate)
    {
        var function = aggregate.Function.Trim().ToLowerInvariant();
        var alias = Quote(Name(aggregate));
        if (function == "count" && string.IsNullOrWhiteSpace(aggregate.Column))
        {
            return "COUNT(*) AS " + alias;
        }

        var column = Quote(Require(schema, aggregate.Column ?? "").Name);
        var body = function switch
        {
            "count" => $"COUNT({column})",
            "sum" => $"SUM({column}::double precision)",
            "avg" => $"AVG({column}::double precision)",
            "min" => $"MIN({column})",
            "max" => $"MAX({column})",
            _ => throw new ArgumentException($"Aggregate '{aggregate.Function}' is not supported."),
        };
        return body + " AS " + alias;
    }

    private static string Predicate(RowSchema schema, RowFilter filter)
    {
        var column = Require(schema, filter.Column);
        var quoted = Quote(column.Name);
        var op = filter.Operator.Trim().ToLowerInvariant();
        if (op == "contains")
        {
            return $"strpos({quoted}::text, {Literal(filter.Value)}) > 0";
        }

        var sql = op switch
        {
            "eq" => "=",
            "ne" => "<>",
            "gt" => ">",
            "gte" => ">=",
            "lt" => "<",
            "lte" => "<=",
            _ => throw new ArgumentException($"Operator '{filter.Operator}' is not supported."),
        };
        var left = column.Type == "number" ? quoted + "::double precision" : quoted;
        return $"{left} {sql} {Literal(filter.Value, column.Type)}";
    }

    private static string Literal(string value, string? type = null)
    {
        if (value is null) { throw new ArgumentException("A filter value is required."); }
        if (value.Contains('\\', StringComparison.Ordinal)) { throw new ArgumentException("A value cannot contain a backslash."); }
        if (type == "number"
            && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            && double.IsFinite(number))
        {
            return number.ToString("G15", CultureInfo.InvariantCulture);
        }

        return "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
    }

    private static string QuoteTable(string table)
    {
        var parts = table.Split('.');
        if (parts.Length is < 1 or > 2) { throw new ArgumentException("A table name is an identifier or schema.identifier."); }
        return string.Join('.', parts.Select(Quote));
    }

    private static string Quote(string name)
    {
        if (name is null || !Identifier.IsMatch(name))
        {
            throw new ArgumentException($"'{name}' is not a plain identifier.");
        }

        return "\"" + name + "\"";
    }

    private static RowColumn Require(RowSchema schema, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) { throw new ArgumentException("A column name is required."); }
        return schema.Columns.FirstOrDefault(column => column.Name == name)
            ?? throw new ArgumentException($"Column '{name}' is not in the schema.");
    }

    private static string Name(RowAggregate aggregate)
        => string.IsNullOrWhiteSpace(aggregate.Alias) ? aggregate.Function.Trim().ToLowerInvariant() : aggregate.Alias;
}
