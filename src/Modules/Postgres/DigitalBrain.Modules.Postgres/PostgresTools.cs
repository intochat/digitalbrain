using System.ComponentModel;
using DigitalBrain.AI.Agents;
using DigitalBrain.Supabase.Tables;
using DigitalBrain.Supabase.Windows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.Postgres;

internal sealed class PostgresTools(IPostgresProvider provider, LiveTableWindows windows,
    PostgresAppResources resources, ILogger<PostgresTools> logger) : IAgentToolFactory
{
    public IReadOnlyList<AIFunction> Create(Func<AgentToolContext> context)
    {
        async Task<object> Schema([Description("Optional PostgreSQL platform table name; omit to list tables.")] string? table = null,
            [Description("Set true to discover tables owned by apps installed in this brain, including provisioned capacity. Returns resource handles and declared schema.")] bool appTables = false, CancellationToken ct = default)
        {
            try
            {
                if (appTables)
                {
                    var discovery = await resources.List(ct);
                    return new
                    {
                        source = "postgres",
                        partial = discovery.Errors.Length > 0,
                        errors = discovery.Errors,
                        resources = discovery.Resources.Select(PostgresAppResources.Describe).ToArray()
                    };
                }
                return await provider.ReadSchemaAsync(table, ct);
            }
            catch (Exception error) when (error is PostgresQueryException or PostgresUnavailableException or UnauthorizedAccessException)
            { return Failure(error); }
        }
        async Task<object> Open([Description("Short window title.")] string title,
            [Description("One read-only SELECT on platform Postgres using discovered schema and explicitly named columns. Omit when resource is provided.")] string? sql = null,
            [Description("Exact app table resource handle returned by postgres_schema(appTables: true). Opens its pinned database using a fixed projection; use table_refine for filters.")] string? resource = null,
            CancellationToken ct = default)
        {
            var trusted = context();
            try
            {
                if (resource is not null)
                {
                    var discovered = await resources.Resolve(resource, ct);
                    return await windows.OpenAsync(trusted.ScopeId, trusted.RunId, trusted.CallId, "Postgres · " + title,
                        PostgresAppLiveSource.Select(discovered), ct, resource);
                }
                if (sql is null) { throw new PostgresQueryException("Provide platform SQL or a discovered app table resource."); }
                return await windows.OpenAsync(trusted.ScopeId, trusted.RunId, trusted.CallId, "Postgres · " + title, sql, ct, "postgres");
            }
            catch (Exception error) when (error is SupabaseTableValidationException or SupabaseTableSourceException)
            { return Failure(error); }
            catch (Exception error) when (error is PostgresQueryException or PostgresUnavailableException or UnauthorizedAccessException)
            { return Failure(error); }
        }
        object Failure(Exception error)
        {
            var trusted = context();
            logger.LogWarning(error, "Postgres tool failed for scope {ScopeId}, run {RunId}, call {CallId}", trusted.ScopeId, trusted.RunId, trusted.CallId);
            return new { isError = true, source = "postgres", message = error.Message, callId = trusted.CallId };
        }
        return [
            AIFunctionFactory.Create(Schema, "postgres_schema", "Discover platform tables or app-owned table resources (appTables: true). App resources use their pinned capacity, which can differ from the platform database."),
            AIFunctionFactory.Create(Open, "show_postgres_query_table", "Open a live interactive table from Postgres in the current workspace. Never substitutes Supabase. Use table_read and table_refine on the returned windowId.")];
    }
}
