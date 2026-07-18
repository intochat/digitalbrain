using Microsoft.Data.Sqlite;

namespace DigitalBrain.SDK.CodeGraph;

public sealed class CodeGraphDatabaseContext
{
    private readonly string _dbPath;

    public CodeGraphDatabaseContext()
    {
        _dbPath = ResolveDatabasePath();
    }

    public string DatabasePath => _dbPath;

    public static string ResolveDatabasePath()
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current != null)
        {
            var dbPath = Path.Combine(current.FullName, ".codegraph", "codegraph.db");
            if (File.Exists(dbPath))
            {
                return dbPath;
            }
            current = current.Parent;
        }

        // Fallback to standard absolute path for the e:\digitalbrain workspace
        var defaultPath = Path.Combine("e:", "digitalbrain", ".codegraph", "codegraph.db");
        if (File.Exists(defaultPath))
        {
            return defaultPath;
        }

        // Final fallback relative to current directory
        return Path.Combine(Directory.GetCurrentDirectory(), ".codegraph", "codegraph.db");
    }

    public async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        // Open as ReadOnly with shared cache for maximum concurrent read performance and zero locking
        var connectionString = $"Data Source={_dbPath};Mode=ReadOnly;Cache=Shared;";
        var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }
}
