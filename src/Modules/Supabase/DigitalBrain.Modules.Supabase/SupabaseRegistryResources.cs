using DigitalBrain.Registry;

namespace DigitalBrain.Supabase;

internal sealed class SupabaseRegistryResources : IRegistryResourceProvider
{
    public string Id => "supabase";
    public Task<RegistryDiscovery> Discover(CancellationToken cancellationToken) => Task.FromResult(new RegistryDiscovery(
        [new("supabase", "Query the configured Supabase connection. Discover actual tables and columns with supabase_schema before opening a query. This connection is distinct from platform Postgres and app-owned pinned storage; do not substitute it for those resources.",
            ["supabase_schema", "show_supabase_query_table", "table_read", "table_refine"])], []));
}
