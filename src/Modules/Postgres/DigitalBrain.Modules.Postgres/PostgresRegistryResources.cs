using DigitalBrain.Registry;

namespace DigitalBrain.Postgres;

internal sealed class PostgresRegistryResources : IRegistryResourceProvider
{
    public string Id => "postgres";
    public RegistryCapability Summary => new(Id, "Postgres database tables, including storage owned by installed apps.", []);
    public Task<RegistryDiscovery> Discover(CancellationToken cancellationToken) => Task.FromResult(new RegistryDiscovery(
        [new(Id, "Call postgres_schema(appTables: true) for app-owned tables, then open the returned exact resource handle with show_postgres_query_table. App tables use pinned capacity; never substitute another database.",
            ["postgres_schema", "show_postgres_query_table", "table_read", "table_refine"])], []));
}
