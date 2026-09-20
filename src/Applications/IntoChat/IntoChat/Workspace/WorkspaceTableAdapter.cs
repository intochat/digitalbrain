using System.Text.Json;
using DigitalBrain.Supabase.Tables;

namespace IntoChat.Workspace;

internal static class WorkspaceTableAdapter
{
    public static object ToJson(SupabaseTableSnapshot table) => new
    {
        table.Id, table.Title, table.Kind, table.Revision, table.Columns,
        Rows = table.Rows.Select(row => new { row.Id, Cells = row.Cells.Select(Parse).ToArray() }).ToArray(),
        Filters = table.Filters.Select(filter => new { filter.ColumnId, filter.Operator, Value = Parse(filter.Value) }).ToArray(),
        table.Sort, table.VisibleColumns, table.TotalRows, table.FilteredRows, table.Offset, table.Limit,
    };
    private static JsonElement Parse(string value) => JsonSerializer.Deserialize<JsonElement>(value);
}

internal sealed record WorkspaceTableFilter(string ColumnId, string Operator, JsonElement Value);
internal sealed record WorkspaceTableView(long ExpectedRevision, IReadOnlyList<WorkspaceTableFilter> Filters,
    SupabaseTableSort? Sort, IReadOnlyList<string> VisibleColumns)
{
    public UpdateSupabaseTableView ToRequest()
    {
        ArgumentNullException.ThrowIfNull(Filters);
        ArgumentNullException.ThrowIfNull(VisibleColumns);
        return new(ExpectedRevision, Filters.Select(f => new SupabaseTableFilter(f.ColumnId, f.Operator,
            f.Value.ValueKind == JsonValueKind.Undefined ? "null" : f.Value.GetRawText())).ToArray(), Sort, VisibleColumns);
    }
}
