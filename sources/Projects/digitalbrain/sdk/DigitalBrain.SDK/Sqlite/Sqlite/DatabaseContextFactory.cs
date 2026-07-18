using System.Text.RegularExpressions;
using DigitalBrain.Runtime;
using Microsoft.Data.Sqlite;

namespace DigitalBrain.SDK.Sqlite.Sqlite;

public sealed partial class DatabaseContextFactory(ILogger<DatabaseContextFactory> logger)
    : IDatabaseContextFactory
{
    // Load-bearing: SqliteNeuron will interpolate user-controlled identifiers
    // (table/column names) into raw SQL because SQLite cannot parameterize
    // identifiers. The slug regex is the SQL-injection guard. The Creator
    // neuron (M6) inherits this rule for any generated SQL.
    static readonly Regex DatabaseIdSlug = SlugRegex();

    public string ResolvePath(string databaseId)
    {
        if (!DatabaseIdSlug.IsMatch(databaseId))
            throw new ArgumentException(
                $"databaseId '{databaseId}' must match {DatabaseIdSlug}", nameof(databaseId));

        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var brainId = BrainScopeHelper.GetActiveScope();
        var directory = Path.Combine(root, "DigitalBrain", "brains", brainId, "databases");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, databaseId + ".db");
    }

    public async Task<SqliteConnection> OpenAsync(string databaseId, CancellationToken cancellationToken)
    {
        var path = ResolvePath(databaseId);
        var connection = new SqliteConnection(
            $"Data Source={path};Mode=ReadWriteCreate;Foreign Keys=true;");
        await connection.OpenAsync(cancellationToken);
        logger.LogDebug("Opened SQLite connection for '{DatabaseId}' at {Path}", databaseId, path);
        return connection;
    }

    [GeneratedRegex(@"^[a-z0-9][a-z0-9-]{0,63}$")]
    private static partial Regex SlugRegex();
}
