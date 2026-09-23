using System.ComponentModel;
using System.Text.Json;
using DigitalBrain.AI.Agents;
using DigitalBrain.Supabase;
using DigitalBrain.Supabase.Tables;
using IntoChat.Workspace;
using Microsoft.Extensions.AI;

namespace IntoChat.Agent;

// The one live-table tool surface: discover schema, open a read-only window, then read and refine
// that same window. Reads obey D6: schema, counts and aggregates always, row values only for
// Public columns (no per-connection grant store exists yet).
internal sealed class WorkspaceTableTools(ISupabaseProvider provider, LiveTableWindows windows) : IAgentToolFactory
{
    public IReadOnlyList<AIFunction> Create(Func<AgentToolContext> context)
    {
        async Task<SupabaseSchema> Schema([Description("Optional database table name; omit to list tables.")] string? table, CancellationToken ct)
            => await provider.ReadSchemaAsync(table, ct);

        async Task<object> Open([Description("Short window title.")] string title,
            [Description("One read-only SELECT using discovered schema and 1–32 explicitly named columns. Select useful columns instead of SELECT *. Rows are paginated automatically; omit LIMIT when the user asks for all records.")] string sql, CancellationToken ct)
        {
            var trusted = context();
            try { return await windows.OpenAsync(trusted.ScopeId, trusted.RunId, trusted.CallId, title, sql, ct); }
            catch (SupabaseTableValidationException error)
            {
                // Let the model repair invalid SQL/projections in this turn. Cancellation
                // and infrastructure failures must still propagate to the run boundary.
                return Failure(error);
            }
        }

        async Task<object> Read([Description("The table id returned when the window opened (its windowId).")] string tableId,
            [Description("Optional aggregate to compute instead of returning rows: count, sum, avg, min or max.")] string? aggregate = null,
            [Description("Column the aggregate applies to; omit for count.")] string? aggregateColumn = null,
            [Description("Optional column ids to project; omit for every readable Public column.")] IReadOnlyList<string>? columns = null,
            [Description("Page size for row values (1–200); ignored when an aggregate is requested.")] int? limit = null,
            CancellationToken ct = default)
        {
            var trusted = context();
            try { return await windows.ReadAsync(trusted.ScopeId, tableId, aggregate, aggregateColumn, columns, limit, ct); }
            catch (Exception error) when (error is SupabaseTableValidationException or SupabaseTableNotFoundException or SupabaseTableSourceException)
            {
                return Failure(error);
            }
        }

        async Task<object> Refine([Description("The table id returned when the window opened (its windowId).")] string tableId,
            [Description("Filters to apply to the same window; replaces the window's current filters.")] IReadOnlyList<TableFilterArgument>? filters = null,
            [Description("Sort column id; omit to keep the current order.")] string? sortColumn = null,
            [Description("Sort descending when true.")] bool sortDescending = false,
            [Description("Optional visible column ids; omit to keep the current set.")] IReadOnlyList<string>? visibleColumns = null,
            CancellationToken ct = default)
        {
            var trusted = context();
            try
            {
                var mapped = (filters ?? []).Select(ToFilter).ToArray();
                var sort = string.IsNullOrWhiteSpace(sortColumn) ? null : new SupabaseTableSort(sortColumn, sortDescending);
                return await windows.RefineAsync(trusted.ScopeId, tableId, mapped, sort, visibleColumns, ct);
            }
            catch (Exception error) when (error is SupabaseTableValidationException or SupabaseTableNotFoundException or SupabaseTableSourceException)
            {
                return Failure(error);
            }
        }

        return [
            AIFunctionFactory.Create(Schema, "supabase_schema", "Discover real Supabase tables and columns before querying."),
            AIFunctionFactory.Create(Open, "show_supabase_query_table", "Open a live interactive query table in the user's current workspace. If isError=true, repair the query and retry with a new tool call."),
            AIFunctionFactory.Create(Read, "table_read", "Read schema, row counts and an optional aggregate from a table window, plus row values for readable Public columns. Rows are never returned for count/sum/avg/min/max. If isError=true, repair the arguments and retry."),
            AIFunctionFactory.Create(Refine, "table_refine", "Refine the existing table window (same window, not a new one) with filters, sort or visible columns. If isError=true, repair the arguments and retry."),
        ];
    }

    private static SupabaseTableFilter ToFilter(TableFilterArgument filter)
    {
        var value = filter.Value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
            ? "null"
            : filter.Value.GetRawText();
        return new(filter.ColumnId, filter.Operator, value);
    }

    private static object Failure(Exception error) => new { isError = true, message = error.Message };
}

// Filters arrive as JSON; Value keeps the exact JSON operand (for example "London", 42 or null).
internal sealed record TableFilterArgument(string ColumnId, string Operator, JsonElement Value);