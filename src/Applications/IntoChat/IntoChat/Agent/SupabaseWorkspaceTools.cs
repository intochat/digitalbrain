using IntoChat.Workspace.Queries;
using System.ComponentModel;
using DigitalBrain.AI.Agents;
using DigitalBrain.Supabase;
using Microsoft.Extensions.AI;

namespace IntoChat.Agent;

internal sealed class SupabaseWorkspaceTools(ISupabaseProvider provider, QueryWindowOperation operations) : IAgentToolFactory
{
    public IReadOnlyList<AIFunction> Create(Func<AgentToolContext> context)
    {
        async Task<SupabaseSchema> Schema([Description("Optional database table name; omit to list tables.")] string? table, CancellationToken ct)
            => await provider.ReadSchemaAsync(table, ct);
        async Task<QueryWindowResult> Show([Description("Short window title.")] string title,
            [Description("One read-only SELECT query using the discovered schema.")] string sql, CancellationToken ct)
        {
            var trusted = context();
            return await operations.ExecuteAsync(trusted.ScopeId, trusted.RunId, trusted.CallId, title, sql, ct);
        }
        return [AIFunctionFactory.Create(Schema, "supabase_schema", "Discover real Supabase tables and columns before querying."),
            AIFunctionFactory.Create(Show, "show_supabase_query_table", "Open a live interactive query table in the user's current workspace.")];
    }
}
