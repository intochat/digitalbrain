using DigitalBrain.Core;
using DigitalBrain.Kernel.Db;
using Microsoft.Data.Sqlite;

namespace DigitalBrain.Kernel;

[GrainType("db.support.v1")]
public class DbSupportNeuron(
    ILogger<DbSupportNeuron> logger,
    NeuronJournals journals,
    SqliteSchemaInspector sqliteSchemaInspector) : Neuron(logger, journals), IDbSupportNeuron
{
    public async Task HandleAsync(DbConnect cmd)
    {
        Logger.LogInformation("DB connected {Name} via {Provider}", cmd.ConnectionName, cmd.Provider);
        // Input already journaled by initiating FireAsync; omit re-fire of handled type to prevent dispatch recursion on echo.
    }

    public async Task HandleAsync(DbQuery cmd)
    {
        if (cmd.Result is not null)
        {
            // This is the echoed result; already journaled. Skip to avoid re-dispatch loop on same IHandle type.
            return;
        }
        Logger.LogInformation("DB query on {Name}: {Q}", cmd.ConnectionName, cmd.Query);
        var result = $"[DB result for {cmd.Query}] 42 rows";
        await FireAsync(new DbQuery(cmd.ConnectionName, cmd.Query, result));
    }

    public async Task HandleAsync(DbInspectSchema cmd)
    {
        if (!IsSqliteRequest(cmd))
        {
            await FireAsync(new DbSchemaInspected(
                cmd.ConnectionName,
                cmd.Provider,
                Schema: null,
                Succeeded: false,
                Error: $"Unsupported database provider '{cmd.Provider}'. Schema inspection currently supports SQLite files.",
                ClientId: cmd.ClientId,
                WorkspaceId: WorkspaceIds.Effective(cmd.WorkspaceId)));
            return;
        }

        try
        {
            Logger.LogInformation(
                "Inspecting DB schema {ConnectionName} provider={Provider} source={Source}",
                cmd.ConnectionName,
                EffectiveProvider(cmd),
                SafeSourceLabel(cmd.SourcePath, cmd.ConnectionString));

            var schema = !string.IsNullOrWhiteSpace(cmd.ConnectionString)
                ? await sqliteSchemaInspector.InspectConnectionStringAsync(
                    cmd.ConnectionString,
                    cmd.ConnectionName,
                    cmd.SourcePath,
                    cmd.ClientId,
                    cmd.WorkspaceId)
                : await sqliteSchemaInspector.InspectFileAsync(
                    cmd.SourcePath ?? string.Empty,
                    cmd.ConnectionName,
                    cmd.SourcePath,
                    cmd.ClientId,
                    cmd.WorkspaceId);

            await FireAsync(new DbSchemaInspected(
                cmd.ConnectionName,
                "sqlite",
                schema,
                Succeeded: true,
                Error: null,
                ClientId: cmd.ClientId,
                WorkspaceId: WorkspaceIds.Effective(cmd.WorkspaceId)));
        }
        catch (Exception ex)
        {
            Logger.LogWarning(
                ex,
                "DB schema inspection failed for {ConnectionName} source={Source}",
                cmd.ConnectionName,
                SafeSourceLabel(cmd.SourcePath, cmd.ConnectionString));

            await FireAsync(new DbSchemaInspected(
                cmd.ConnectionName,
                EffectiveProvider(cmd),
                Schema: null,
                Succeeded: false,
                Error: ex.GetBaseException().Message,
                ClientId: cmd.ClientId,
                WorkspaceId: WorkspaceIds.Effective(cmd.WorkspaceId)));
        }
    }

    private static bool IsSqliteRequest(DbInspectSchema cmd) =>
        string.Equals(cmd.Provider, "sqlite", StringComparison.OrdinalIgnoreCase) ||
        IsSqlitePath(cmd.SourcePath) ||
        IsSqliteConnectionString(cmd.ConnectionString);

    private static string EffectiveProvider(DbInspectSchema cmd) =>
        IsSqliteRequest(cmd) ? "sqlite" : cmd.Provider;

    private static bool IsSqlitePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        var extension = Path.GetExtension(path);
        return extension.Equals(".db", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".sqlite", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".sqlite3", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSqliteConnectionString(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return false;

        try
        {
            var builder = new SqliteConnectionStringBuilder(connectionString);
            return builder.DataSource == ":memory:" || IsSqlitePath(builder.DataSource);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static string SafeSourceLabel(string? sourcePath, string? connectionString)
    {
        if (!string.IsNullOrWhiteSpace(sourcePath))
            return SafeFileName(sourcePath);

        if (string.IsNullOrWhiteSpace(connectionString))
            return "none";

        try
        {
            var builder = new SqliteConnectionStringBuilder(connectionString);
            return SafeFileName(builder.DataSource);
        }
        catch (ArgumentException)
        {
            return "connection-string";
        }
    }

    private static string SafeFileName(string path)
    {
        try
        {
            var fileName = Path.GetFileName(path);
            return string.IsNullOrWhiteSpace(fileName) ? "sqlite" : fileName;
        }
        catch (ArgumentException)
        {
            return "sqlite";
        }
    }
}
