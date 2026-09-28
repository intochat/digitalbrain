using System.Globalization;
using System.Text.Json;
using DigitalBrain.Supabase.Tables;

namespace DigitalBrain.Supabase;

internal sealed record CompiledQuery(string PageSql, string FilteredCountSql, string TotalCountSql, IReadOnlyList<object> Parameters);

internal sealed record CompiledAggregate(string Sql, IReadOnlyList<object> Parameters);

internal static class QueryPlanCompiler
{
    private static readonly string[] AggregateFunctions = ["count", "sum", "avg", "min", "max"];

    public static CompiledQuery Compile(QueryPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        SupabaseTablePolicy.ValidatePage(plan.Offset, plan.Limit);
        var columns = plan.Columns.ToDictionary(column => column.Name, StringComparer.Ordinal);
        var parameters = new List<object>();
        var predicates = Predicates(plan, columns, parameters);
        var from = $"FROM ({plan.BaseSql}) AS q";
        var where = predicates.Count == 0 ? string.Empty : " WHERE " + string.Join(" AND ", predicates.Select(p => $"({p})"));
        var order = new List<string>();
        if (plan.Sort is { } sort)
        {
            if (!columns.TryGetValue(sort.ColumnId, out var column)) { throw new SupabaseTableValidationException("Unknown sort column."); }
            order.Add(OrderColumn(column) + (sort.Descending ? " DESC NULLS LAST" : " ASC NULLS FIRST"));
        }
        // Explicit outer ordering gives stable pages; use the view's sort to select the desired order.
        order.AddRange(plan.Columns.Where(c => c.Name != plan.Sort?.ColumnId).Select(c => OrderColumn(c) + " ASC NULLS FIRST"));
        var orderBy = order.Count == 0 ? string.Empty : " ORDER BY " + string.Join(", ", order);
        return new($"SELECT * {from}{where}{orderBy} LIMIT {plan.Limit.ToString(CultureInfo.InvariantCulture)} OFFSET {plan.Offset.ToString(CultureInfo.InvariantCulture)}",
            $"SELECT count(*) {from}{where}", $"SELECT count(*) {from}", parameters);
    }

    public static CompiledAggregate CompileAggregate(QueryPlan plan, string function, string columnId)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!AggregateFunctions.Contains(function, StringComparer.Ordinal))
        { throw new SupabaseTableValidationException("Aggregates are count, sum, avg, min and max."); }
        var columns = plan.Columns.ToDictionary(column => column.Name, StringComparer.Ordinal);
        var parameters = new List<object>();
        var predicates = Predicates(plan, columns, parameters);
        var from = $"FROM ({plan.BaseSql}) AS q";
        var where = predicates.Count == 0 ? string.Empty : " WHERE " + string.Join(" AND ", predicates.Select(p => $"({p})"));
        string expression;
        if (function == "count") { expression = "count(*)"; }
        else
        {
            if (!columns.TryGetValue(columnId, out var column)) { throw new SupabaseTableValidationException("Unknown aggregate column."); }
            if (function is "sum" or "avg" && column.TableType != SupabaseTypeMap.Number)
            { throw new SupabaseTableValidationException("sum and avg require a number column."); }
            if (function is "min" or "max" && column.TableType is not (SupabaseTypeMap.Number or SupabaseTypeMap.Date))
            { throw new SupabaseTableValidationException("min and max require a number or date column."); }
            expression = $"{function}({Quote(column.Name)})";
        }
        return new($"SELECT {expression} {from}{where}", parameters);
    }

    private static List<string> Predicates(QueryPlan plan, Dictionary<string, SupabaseColumn> columns, List<object> parameters)
    {
        var predicates = new List<string>();
        foreach (var filter in plan.Filters)
        {
            if (!columns.TryGetValue(filter.ColumnId, out var column)) { throw new SupabaseTableValidationException("Unknown filter column."); }
            if (!SupabaseTablePolicy.TryElement(filter.Value, out var value)) { throw new SupabaseTableValidationException($"Filter value for '{filter.ColumnId}' is not valid JSON."); }
            predicates.Add(Predicate(column, filter, value, parameters));
        }
        return predicates;
    }

    private static string OrderColumn(SupabaseColumn column)
        => column.TableType == SupabaseTypeMap.Text ? $"CAST({Quote(column.Name)} AS text)" : Quote(column.Name);

    private static string Predicate(SupabaseColumn column, SupabaseTableFilter filter, JsonElement value, List<object> parameters)
    {
        var name = Quote(column.Name);
        var missing = value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined;
        if (filter.Operator == "isNull" || (filter.Operator == "eq" && missing)) { return $"{name} IS NULL"; }
        if (filter.Operator == "isNotNull" || (filter.Operator == "neq" && missing)) { return $"{name} IS NOT NULL"; }
        object operand = column.TableType switch
        {
            SupabaseTypeMap.Number => value.GetDecimal(),
            SupabaseTypeMap.Boolean => value.GetBoolean(),
            SupabaseTypeMap.Date => DateOnly.ParseExact(value.GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture),
            _ => value.ValueKind == JsonValueKind.String ? value.GetString()! : value.GetRawText(),
        };
        parameters.Add(operand);
        var placeholder = "$" + parameters.Count.ToString(CultureInfo.InvariantCulture);
        if (column.TableType == SupabaseTypeMap.Text)
        {
            var text = $"lower(CAST({name} AS text))";
            return filter.Operator switch
            {
                "eq" => $"{text} = lower({placeholder}::text)",
                "neq" => $"{name} IS NULL OR {text} <> lower({placeholder}::text)",
                "contains" => $"strpos({text}, lower({placeholder}::text)) > 0",
                _ => throw new SupabaseTableValidationException("Unsupported text filter operator."),
            };
        }
        return filter.Operator switch
        {
            "eq" => $"{name} = {placeholder}",
            "neq" => $"{name} IS NULL OR {name} <> {placeholder}",
            "gt" => $"{name} > {placeholder}",
            "gte" => $"{name} >= {placeholder}",
            "lt" => $"{name} < {placeholder}",
            "lte" => $"{name} <= {placeholder}",
            _ => throw new SupabaseTableValidationException("Unsupported filter operator."),
        };
    }

    private static string Quote(string name) => "\"" + name.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}