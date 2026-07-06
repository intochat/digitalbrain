using Microsoft.Data.Sqlite;

namespace DigitalBrain.Tests.Db;

internal static class SqliteTestDatabases
{
    public static async Task<string> CreateBudgetDatabaseAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), "digitalbrain-budget-" + Guid.NewGuid().ToString("N") + ".db");
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();

        using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        await ExecuteAsync(connection, "PRAGMA foreign_keys = ON;");
        await ExecuteAsync(connection, """
            CREATE TABLE accounts (
                id INTEGER PRIMARY KEY,
                name TEXT NOT NULL
            );
            """);
        await ExecuteAsync(connection, """
            CREATE TABLE transactions (
                id INTEGER PRIMARY KEY,
                account_id INTEGER NOT NULL,
                amount REAL NOT NULL DEFAULT 0,
                memo TEXT,
                FOREIGN KEY (account_id) REFERENCES accounts(id) ON DELETE CASCADE
            );
            """);
        await ExecuteAsync(connection, "CREATE INDEX ix_transactions_account_id ON transactions(account_id);");

        return path;
    }

    public static void DeleteQuietly(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
        }
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
