using System.Text.Json;
using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Kernel.Enforcement;
using DigitalBrain.Registry;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.Postgres;

internal sealed class PostgresRegistryResources(PostgresAppResources resources, ILogger<PostgresRegistryResources> logger) : IRegistryResourceProvider
{
    public string Id => "postgres";
    public async Task<RegistryDiscovery> Discover(CancellationToken cancellationToken)
    {
        var caller = CallerContextStamper.Require();
        if (!CallerContextStamper.IsTrusted(caller) || caller.Kind == CallerKind.App)
        { throw new UnauthorizedAccessException("Only a trusted host may discover app storage."); }
        string? resourcesJson = null;
        RegistryDiscoveryError[] errors;
        try
        {
            var result = await resources.List(cancellationToken);
            resourcesJson = JsonSerializer.Serialize(result.Resources.Select(resource => new
            { resource = PostgresAppResources.Source(resource), resource.AppId, resource.TableId, resource.Origin, columns = resource.Table.Definition.Columns }));
            errors = [.. result.Errors.Select(error => new RegistryDiscoveryError(Id, "An installed app table could not be described: " + error.TableId))];
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            logger.LogWarning(error, "Could not discover installed app Postgres resources");
            errors = [new(Id, "Installed app table resources are temporarily unavailable. Platform Postgres tools remain available.")];
        }
        return new([new("postgres", "Postgres platform and installed app storage. Call postgres_schema(appTables: true) for app-owned tables and open their exact resource handles with show_postgres_query_table. App tables use pinned capacity which may differ from platform Postgres. Never substitute Supabase.",
            ["postgres_schema", "show_postgres_query_table", "table_read", "table_refine"], resourcesJson)], errors);
    }
}
