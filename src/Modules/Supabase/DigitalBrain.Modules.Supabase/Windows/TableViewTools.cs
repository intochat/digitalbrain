using System.ComponentModel;
using System.Text.Json;
using DigitalBrain.AI.Agents;
using DigitalBrain.Supabase.Tables;
using Microsoft.Extensions.AI;

namespace DigitalBrain.Supabase.Windows;

internal sealed class TableViewTools(LiveTableWindows windows) : IAgentToolFactory
{
    public IReadOnlyList<AIFunction> Create(Func<AgentToolContext> context)
    {
        async Task<object> Read([Description("The table id returned when the window opened (its windowId).")] string tableId,
            [Description("Optional aggregate to compute instead of returning rows: count, sum, avg, min or max.")] string? aggregate = null,
            [Description("Column the aggregate applies to; omit for count.")] string? aggregateColumn = null,
            [Description("Optional column ids to project; omit for every readable Public column.")] IReadOnlyList<string>? columns = null,
            [Description("Page size for row values (1–200); ignored when an aggregate is requested.")] int? limit = null,
            CancellationToken ct = default)
        {
            var trusted = context();
            try { return await windows.ReadAsync(trusted.ScopeId, tableId, aggregate, aggregateColumn, columns, limit, ct, trusted.DatabaseSource); }
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
                var mapped = (filters ?? []).Select(filter => LiveTableJson.Filter(filter.ColumnId, filter.Operator, filter.Value)).ToArray();
                var sort = string.IsNullOrWhiteSpace(sortColumn) ? null : new SupabaseTableSort(sortColumn, sortDescending);
                return await windows.RefineAsync(trusted.ScopeId, tableId, mapped, sort, visibleColumns, ct, trusted.DatabaseSource);
            }
            catch (Exception error) when (error is SupabaseTableValidationException or SupabaseTableNotFoundException or SupabaseTableSourceException)
            {
                return Failure(error);
            }
        }

        return [
            AIFunctionFactory.Create(Read, "table_read", "Read schema, row counts and an optional aggregate from a table window, plus row values for readable Public columns. Rows are never returned for count/sum/avg/min/max. If isError=true, repair the arguments and retry."),
            AIFunctionFactory.Create(Refine, "table_refine", "Refine the existing table window (same window, not a new one) with filters, sort or visible columns. If isError=true, repair the arguments and retry."),
        ];
    }
    private static object Failure(Exception error) => new { isError = true, message = error.Message };
}

internal sealed record TableFilterArgument(string ColumnId, string Operator, JsonElement Value);
