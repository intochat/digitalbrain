using System.Globalization;
using System.Text.Json;
using DigitalBrain.Flutter;

namespace DigitalBrain.Supabase;

internal sealed record CompiledQuery(string PageSql, string FilteredCountSql, string TotalCountSql, IReadOnlyList<object> Parameters);

internal static class QueryPlanCompiler
{
    public static CompiledQuery Compile(QueryPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        TablePolicy.ValidatePage(plan.Offset, plan.Limit);
        var columns = plan.Columns.ToDictionary(column => column.Name, StringComparer.Ordinal);
        var parameters = new List<object>();
        var predicates = new List<string>();
        foreach (var filter in plan.Filters)
        {
            if (!columns.TryGetValue(filter.ColumnId, out var column)) { throw new TableValidationException("Unknown filter column."); }
            predicates.Add(Predicate(column, filter, parameters));
        }
        var from = $"FROM ({plan.BaseSql}) AS q";
        var where = predicates.Count == 0 ? string.Empty : " WHERE " + string.Join(" AND ", predicates.Select(p => $"({p})"));
        var order = new List<string>();
        if (plan.Sort is { } sort)
        {
            if (!columns.TryGetValue(sort.ColumnId, out var column)) { throw new TableValidationException("Unknown sort column."); }
            order.Add(OrderColumn(column) + (sort.Descending ? " DESC NULLS LAST" : " ASC NULLS FIRST"));
        }
        // Explicit outer ordering gives stable pages; use the view's sort to select the desired order.
        order.AddRange(plan.Columns.Where(c => c.Name != plan.Sort?.ColumnId).Select(c => OrderColumn(c) + " ASC NULLS FIRST"));
        var orderBy = order.Count == 0 ? string.Empty : " ORDER BY " + string.Join(", ", order);
        return new($"SELECT * {from}{where}{orderBy} LIMIT {plan.Limit.ToString(CultureInfo.InvariantCulture)} OFFSET {plan.Offset.ToString(CultureInfo.InvariantCulture)}",
            $"SELECT count(*) {from}{where}", $"SELECT count(*) {from}", parameters);
    }

    private static string OrderColumn(SupabaseColumn column)
        => column.TableType == SupabaseTypeMap.Text ? $"CAST({Quote(column.Name)} AS text)" : Quote(column.Name);

    private static string Predicate(SupabaseColumn column, TableFilter filter, List<object> parameters)
    {
        var name = Quote(column.Name);
        var missing = filter.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined;
        if (filter.Operator == "isNull" || (filter.Operator == "eq" && missing)) { return $"{name} IS NULL"; }
        if (filter.Operator == "isNotNull" || (filter.Operator == "neq" && missing)) { return $"{name} IS NOT NULL"; }
        object value = column.TableType switch
        {
            SupabaseTypeMap.Number => filter.Value.GetDecimal(),
            SupabaseTypeMap.Boolean => filter.Value.GetBoolean(),
            SupabaseTypeMap.Date => DateOnly.ParseExact(filter.Value.GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture),
            _ => filter.Value.ValueKind == JsonValueKind.String ? filter.Value.GetString()! : filter.Value.GetRawText(),
        };
        parameters.Add(value);
        var operand = "$" + parameters.Count.ToString(CultureInfo.InvariantCulture);
        if (column.TableType == SupabaseTypeMap.Text)
        {
            var text = $"lower(CAST({name} AS text))";
            return filter.Operator switch
            {
                "eq" => $"{text} = lower({operand}::text)",
                "neq" => $"{name} IS NULL OR {text} <> lower({operand}::text)",
                "contains" => $"strpos({text}, lower({operand}::text)) > 0",
                _ => throw new TableValidationException("Unsupported text filter operator."),
            };
        }
        return filter.Operator switch
        {
            "eq" => $"{name} = {operand}",
            "neq" => $"{name} IS NULL OR {name} <> {operand}",
            "gt" => $"{name} > {operand}",
            "gte" => $"{name} >= {operand}",
            "lt" => $"{name} < {operand}",
            "lte" => $"{name} <= {operand}",
            _ => throw new TableValidationException("Unsupported filter operator."),
        };
    }

    private static string Quote(string name) => "\"" + name.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
