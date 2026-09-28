using System.Text.Json;
using DigitalBrain.Supabase.Tables;

namespace DigitalBrain.Supabase.Windows;

public static class LiveTableJson
{
    public static object ToJson(SupabaseTableSnapshot table) => new
    {
        table.Id,
        table.Title,
        table.Kind,
        table.Revision,
        table.Columns,
        Rows = table.Rows.Select(row => new { row.Id, Cells = row.Cells.Select(Parse).ToArray() }).ToArray(),
        Filters = table.Filters.Select(filter => new { filter.ColumnId, filter.Operator, Value = Parse(filter.Value) }).ToArray(),
        table.Sort,
        table.VisibleColumns,
        table.TotalRows,
        table.FilteredRows,
        table.Offset,
        table.Limit,
    };

    // A JSON operand keeps its exact text (for example "London", 42 or null); a missing one is null.
    public static SupabaseTableFilter Filter(string columnId, string op, JsonElement value)
        => new(columnId, op, value.ValueKind == JsonValueKind.Undefined ? "null" : value.GetRawText());

    private static JsonElement Parse(string value) => JsonSerializer.Deserialize<JsonElement>(value);
}

public sealed record LiveTableFilter(string ColumnId, string Operator, JsonElement Value);
public sealed record LiveTableView(long ExpectedRevision, IReadOnlyList<LiveTableFilter> Filters,
    SupabaseTableSort? Sort, IReadOnlyList<string> VisibleColumns)
{
    public UpdateSupabaseTableView ToRequest()
    {
        ArgumentNullException.ThrowIfNull(Filters);
        ArgumentNullException.ThrowIfNull(VisibleColumns);
        return new(ExpectedRevision, Filters.Select(f => LiveTableJson.Filter(f.ColumnId, f.Operator, f.Value)).ToArray(), Sort, VisibleColumns);
    }
}