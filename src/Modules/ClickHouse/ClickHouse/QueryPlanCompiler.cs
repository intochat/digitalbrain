using System.Text.Json;
using System.Text.RegularExpressions;
using DigitalBrain.UI;

namespace DigitalBrain.ClickHouse;

internal sealed record CompiledQuery(
    string PageSql,
    string FilteredCountSql,
    string TotalCountSql,
    IReadOnlyDictionary<string, object> Parameters);

// Compiles a ui view into SQL over the base query. Column names travel as {name:Identifier}
// parameters and values as typed parameters, so no user text is ever spliced into the SQL.
// Predicates mirror TablePolicy: text compares case-insensitively, neq keeps nulls, ordered
// comparisons drop them, and sorting puts nulls first ascending and last descending.
internal static partial class QueryPlanCompiler
{
    public static CompiledQuery Compile(QueryPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var columns = plan.Columns.ToDictionary(column => column.Name, StringComparer.Ordinal);
        var parameters = new Dictionary<string, object>(StringComparer.Ordinal);
        var predicates = new List<string>();
        for (var index = 0; index < plan.Filters.Count; index++)
        {
            var filter = plan.Filters[index];
            if (!columns.TryGetValue(filter.ColumnId, out var column))
            {
                throw new TableValidationException($"Filter column '{filter.ColumnId}' does not exist.");
            }

            parameters[$"c{index}"] = column.Name;
            predicates.Add(Predicate(column.TableType, filter, $"{{c{index}:Identifier}}", $"p{index}", parameters));
        }

        var from = $"FROM ({plan.BaseSql}) AS q";
        var where = predicates.Count == 0 ? string.Empty : " WHERE " + string.Join(" AND ", predicates.Select(predicate => $"({predicate})"));
        var orderBy = OrderBy(plan, columns, parameters);
        parameters["limit"] = (ulong)plan.Limit;
        parameters["offset"] = (ulong)plan.Offset;
        return new(
            $"SELECT * {from}{where}{orderBy} LIMIT {{limit:UInt64}} OFFSET {{offset:UInt64}}",
            $"SELECT count() {from}{where}",
            $"SELECT count() {from}",
            parameters);
    }

    private static string Predicate(string tableType, TableFilter filter, string column, string parameter, Dictionary<string, object> parameters)
    {
        var missing = filter.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined;
        switch (filter.Operator)
        {
            case "isNull":
            case "eq" when missing:
                return $"{column} IS NULL";
            case "isNotNull":
            case "neq" when missing:
                return $"{column} IS NOT NULL";
        }

        var operand = Operand(tableType, filter.Value, parameter, parameters);
        if (tableType == ClickHouseTypeMap.Text)
        {
            var text = $"lowerUTF8(toString({column}))";
            return filter.Operator switch
            {
                "eq" => $"{text} = lowerUTF8({operand})",
                "neq" => $"{column} IS NULL OR {text} != lowerUTF8({operand})",
                "contains" => $"positionCaseInsensitiveUTF8(toString({column}), {operand}) > 0",
                _ => throw new TableValidationException($"Operator '{filter.Operator}' is not supported for text."),
            };
        }

        return filter.Operator switch
        {
            "eq" => $"{column} = {operand}",
            "neq" => $"{column} IS NULL OR {column} != {operand}",
            "gt" => $"{column} > {operand}",
            "gte" => $"{column} >= {operand}",
            "lt" => $"{column} < {operand}",
            "lte" => $"{column} <= {operand}",
            _ => throw new TableValidationException($"Operator '{filter.Operator}' is not supported for {tableType}."),
        };
    }

    private static string Operand(string tableType, JsonElement value, string parameter, Dictionary<string, object> parameters)
    {
        switch (tableType)
        {
            case ClickHouseTypeMap.Number:
                parameters[parameter] = value.GetDouble();
                return $"{{{parameter}:Float64}}";
            case ClickHouseTypeMap.Date:
                parameters[parameter] = value.GetString() ?? throw new TableValidationException("Date filters need a yyyy-MM-dd value.");
                return $"{{{parameter}:Date}}";
            case ClickHouseTypeMap.Boolean:
                parameters[parameter] = value.GetBoolean();
                return $"{{{parameter}:Bool}}";
            default:
                parameters[parameter] = value.ValueKind == JsonValueKind.String ? value.GetString()! : value.GetRawText();
                return $"{{{parameter}:String}}";
        }
    }

    // A view sort leads the ORDER BY and every other orderable column follows as a tiebreaker, so
    // LIMIT/OFFSET pages never overlap or skip rows on duplicate keys. Without a view sort a base
    // query that orders itself flows through the wrapper untouched (a plain wrapper keeps subquery
    // order), and one that does not gets the tiebreakers alone so its pages are stable too.
    private static string OrderBy(QueryPlan plan, Dictionary<string, ClickHouseColumn> columns, Dictionary<string, object> parameters)
    {
        var terms = new List<string>();
        if (plan.Sort is { } sort)
        {
            if (!columns.ContainsKey(sort.ColumnId))
            {
                throw new TableValidationException($"Sort column '{sort.ColumnId}' does not exist.");
            }

            parameters["sort"] = sort.ColumnId;
            terms.Add(sort.Descending ? "{sort:Identifier} DESC NULLS LAST" : "{sort:Identifier} ASC NULLS FIRST");
        }
        else if (OrderByClause().IsMatch(ClickHouseQueryGuard.MaskQuoted(plan.BaseSql)))
        {
            return string.Empty;
        }

        var tiebreakers = 0;
        foreach (var column in plan.Columns)
        {
            if (column.Name == plan.Sort?.ColumnId || !ClickHouseTypeMap.IsOrderable(column.ClickHouseType))
            {
                continue;
            }

            parameters[$"t{tiebreakers}"] = column.Name;
            terms.Add($"{{t{tiebreakers}:Identifier}}");
            tiebreakers++;
        }

        return terms.Count == 0 ? string.Empty : " ORDER BY " + string.Join(", ", terms);
    }

    [GeneratedRegex(@"\bORDER\s+BY\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OrderByClause();
}
