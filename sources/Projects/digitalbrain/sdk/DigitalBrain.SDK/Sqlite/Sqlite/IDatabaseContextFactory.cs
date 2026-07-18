using Microsoft.Data.Sqlite;

namespace DigitalBrain.SDK.Sqlite.Sqlite;

public interface IDatabaseContextFactory
{
    Task<SqliteConnection> OpenAsync(string databaseId, CancellationToken cancellationToken);
    string ResolvePath(string databaseId);
}
