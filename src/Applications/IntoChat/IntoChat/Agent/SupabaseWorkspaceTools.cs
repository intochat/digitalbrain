using System.ComponentModel;
using DigitalBrain.AI.Agents;
using DigitalBrain.Supabase;
using DigitalBrain.Supabase.Tables;
using IntoChat.Workspace.Queries;
using Microsoft.Extensions.AI;

namespace IntoChat.Agent;

internal sealed class SupabaseWorkspaceTools(ISupabaseProvider provider, QueryWindowOperation operations) : IAgentToolFactory
{
    public IReadOnlyList<AIFunction> Create(Func<AgentToolContext> context)
    {
        async Task<SupabaseSchema> Schema([Description("Optional database table name; omit to list tables.")] string? table, CancellationToken ct)
            => await provider.ReadSchemaAsync(table, ct);
        async Task<object> Show([Description("Short window title.")] string title,
            [Description("One read-only SELECT using discovered schema and 1–32 explicitly named columns. Select useful columns instead of SELECT *. Rows are paginated automatically; omit LIMIT when the user asks for all records.")] string sql, CancellationToken ct)
        {
            var trusted = context();
            try { return await operations.ExecuteAsync(trusted.ScopeId, trusted.RunId, trusted.CallId, title, sql, ct); }
            catch (SupabaseTableValidationException error)
            {
                // Let the model repair invalid SQL/projections in this turn. Cancellation
                // and infrastructure failures must still propagate to the run boundary.
                return new { isError = true, message = error.Message };
            }
        }
        return [AIFunctionFactory.Create(Schema, "supabase_schema", "Discover real Supabase tables and columns before querying."),
            AIFunctionFactory.Create(Show, "show_supabase_query_table", "Open a live interactive query table in the user's current workspace. If isError=true, repair the query and retry with a new tool call.")];
    }
}
