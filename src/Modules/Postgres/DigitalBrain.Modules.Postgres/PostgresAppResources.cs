using System.Text;
using System.Text.Json;
using DigitalBrain.Apps;
using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Kernel.Enforcement;

namespace DigitalBrain.Postgres;

internal sealed record PostgresAppDiscovery(PostgresAppTableResource[] Resources, PostgresAppDiscoveryError[] Errors);
internal sealed record PostgresAppDiscoveryError(string AppId, string TableId, string Message);

internal sealed class PostgresAppResources(DigitalBrain.IDigitalBrain brain)
{
    public const string SourcePrefix = "postgres-app:";

    public static string Source(PostgresAppTableResource resource)
        => SourcePrefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new[] { resource.AppId, resource.TableId })));

    public static object Describe(PostgresAppTableResource resource) => new
    {
        resource = Source(resource),
        appId = resource.AppId,
        storageTableId = resource.TableId,
        origin = resource.Origin,
        columns = resource.Table.Definition.Columns,
        open = new { tool = "show_postgres_query_table", arguments = new { title = "App data", resource = Source(resource) } },
        usage = "Call open.tool with open.arguments to display this data. Use its returned windowId for table_read/table_refine; storageTableId is not a windowId.",
    };

    private static string Scope()
    {
        var caller = CallerContextStamper.Require();
        if (!CallerContextStamper.IsTrusted(caller) || caller.Kind == CallerKind.App)
        { throw new UnauthorizedAccessException("Only a trusted host may discover app storage."); }
        return BrainScope.CurrentId();
    }

    private async Task<string[]> Installed(string scope, CancellationToken ct)
    {
        var packages = await brain.Get<IApps>(scope).List().WaitAsync(ct);
        var installed = new List<string>();
        foreach (var package in packages)
        {
            var id = scope + "/packages/" + package;
            var snapshot = await brain.Get<IApp>(id).Read().WaitAsync(ct);
            if (snapshot.Status == AppStatus.Installed && !snapshot.UninstallPending) { installed.Add(id); }
        }
        return installed.ToArray();
    }

    public async Task<PostgresAppDiscovery> List(CancellationToken ct)
        => await CollectInstalled(await Installed(Scope(), ct), ct);

    internal async Task<PostgresAppDiscovery> CollectInstalled(string[] installed, CancellationToken ct)
    {
        var scope = Scope();
        var resources = new List<PostgresAppTableResource>();
        var errors = new List<PostgresAppDiscoveryError>();
        foreach (var app in installed)
        {
            var owner = JsonSerializer.Serialize(new[] { scope, app });
            foreach (var table in await brain.Get<IPostgresAppStorage>(owner).ReadTables().WaitAsync(ct))
            {
                try { resources.Add(await brain.Get<IPostgresTableResource>(table).DescribeResource(owner).WaitAsync(ct)); }
                catch (PostgresQueryException error) { errors.Add(new(app, table, error.Message)); }
            }
        }
        return new(resources.ToArray(), errors.ToArray());
    }

    public async Task<PostgresAppTableResource> Resolve(string source, CancellationToken ct)
    {
        var scope = Scope();
        string[] identity;
        try
        {
            if (!source.StartsWith(SourcePrefix, StringComparison.Ordinal) || source.Length > 8192) { throw new FormatException(); }
            identity = JsonSerializer.Deserialize<string[]>(Encoding.UTF8.GetString(Convert.FromBase64String(source[SourcePrefix.Length..])))!;
            if (identity is not { Length: 2 } || identity.Any(string.IsNullOrWhiteSpace)) { throw new FormatException(); }
        }
        catch (Exception error) when (error is FormatException or JsonException or ArgumentException)
        { throw new PostgresQueryException("Select an app table resource returned by postgres_schema."); }
        if (!(await Installed(scope, ct)).Contains(identity[0], StringComparer.Ordinal))
        { throw new UnauthorizedAccessException("This app is not installed in the current brain."); }
        var owner = JsonSerializer.Serialize(new[] { scope, identity[0] });
        var tables = await brain.Get<IPostgresAppStorage>(owner).ReadTables().WaitAsync(ct);
        if (!tables.Contains(identity[1], StringComparer.Ordinal))
        { throw new UnauthorizedAccessException("This table is not owned by the installed app."); }
        return await brain.Get<IPostgresTableResource>(identity[1]).DescribeResource(owner).WaitAsync(ct);
    }
}
