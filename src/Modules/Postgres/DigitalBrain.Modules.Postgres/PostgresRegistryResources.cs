using System.Text.Json;
using DigitalBrain.Registry;

namespace DigitalBrain.Postgres;

internal sealed class PostgresRegistryResources(PostgresAppResources resources) : IRegistryResourceProvider
{
    public string Id => "postgres";
    public async Task<RegistryDiscovery> Discover(CancellationToken cancellationToken)
    {
        var result = await resources.List(cancellationToken);
        return new([new("postgres", "Postgres platform and installed app storage. Call postgres_schema(appTables: true) for app-owned tables and open their exact resource handles with show_postgres_query_table. App tables use pinned capacity which may differ from platform Postgres. Never substitute Supabase.",
            ["postgres_schema", "show_postgres_query_table", "table_read", "table_refine"],
            JsonSerializer.Serialize(result.Resources.Select(resource => new
            { resource = PostgresAppResources.Source(resource), resource.AppId, resource.TableId, resource.Origin, columns = resource.Table.Definition.Columns })))],
            [.. result.Errors.Select(error => new RegistryDiscoveryError(Id, "An installed app table could not be described: " + error.TableId))]);
    }
}
