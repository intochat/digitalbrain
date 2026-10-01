using System.ComponentModel;
using DigitalBrain.AI.Agents;
using DigitalBrain.Supabase.Tables;
using DigitalBrain.Supabase.Windows;
using Microsoft.Extensions.AI;

namespace DigitalBrain.Postgres;

internal sealed class PostgresTools(IPostgresProvider provider, LiveTableWindows windows) : IAgentToolFactory
{
    public IReadOnlyList<AIFunction> Create(Func<AgentToolContext> context)
    {
        async Task<object> Schema([Description("Optional PostgreSQL table name; omit to list tables.")] string? table = null, CancellationToken ct = default)
        {
            try { return await provider.ReadSchemaAsync(table, ct); }
            catch (PostgresQueryException error) { return new { isError = true, source = "postgres", message = error.Message }; }
        }
        async Task<object> Open([Description("Short window title.")] string title,
            [Description("One read-only SELECT on the Postgres database using discovered schema and 1–32 explicitly named columns. Rows are paginated automatically.")] string sql,
            CancellationToken ct)
        {
            var trusted = context();
            try { return await windows.OpenAsync(trusted.ScopeId, trusted.RunId, trusted.CallId, "Postgres · " + title, sql, ct, "postgres"); }
            catch (Exception error) when (error is SupabaseTableValidationException or SupabaseTableSourceException)
            { return new { isError = true, source = "postgres", message = error.Message }; }
        }
        return [
            AIFunctionFactory.Create(Schema, "postgres_schema", "Discover tables and columns in the Postgres module's database. This is a separate connection from Supabase."),
            AIFunctionFactory.Create(Open, "show_postgres_query_table", "Open a live interactive table from Postgres in the current workspace. Never substitutes Supabase. Use table_read and table_refine on the returned windowId.")];
    }
}
