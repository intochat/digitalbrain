using DigitalBrain.Core;
using Microsoft.Data.Sqlite;

namespace DigitalBrain.Kernel.Db;

public sealed class SqliteSchemaInspector(ILogger<SqliteSchemaInspector> logger)
{
    public const int MaxSchemaObjects = 256;
    public const int MaxColumns = 2048;
    public const int MaxForeignKeys = 2048;
    public const int MaxIndexes = 2048;

    private const int CommandTimeoutSeconds = 5;

    public Task<DbSchemaModel> InspectFileAsync(
        string path,
        string connectionName,
        string? sourcePath = null,
        string? sessionId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("SQLite database path is required.", nameof(path));

        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("SQLite database file was not found.", fullPath);

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = fullPath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        };

        return InspectConnectionStringAsync(
            builder.ToString(),
            connectionName,
            sourcePath ?? path,
            sessionId,
            cancellationToken);
    }

    public async Task<DbSchemaModel> InspectConnectionStringAsync(
        string connectionString,
        string connectionName,
        string? sourcePath = null,
        string? sessionId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("SQLite connection string is required.", nameof(connectionString));

        var builder = new SqliteConnectionStringBuilder(connectionString)
        {
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        };

        if (string.IsNullOrWhiteSpace(builder.DataSource))
            throw new ArgumentException("SQLite connection string must include Data Source.", nameof(connectionString));

        using var connection = new SqliteConnection(builder.ToString());
        await connection.OpenAsync(cancellationToken);
        await ExecuteNonQueryAsync(connection, "PRAGMA query_only = ON;", cancellationToken);

        var sqliteVersion = await ExecuteScalarStringAsync(connection, "SELECT sqlite_version();", cancellationToken);
        var objects = await ReadSchemaObjectsAsync(connection, cancellationToken);

        var tables = new List<DbTable>(objects.Count);
        var columnCount = 0;
        var foreignKeyCount = 0;
        var indexCount = 0;

        foreach (var obj in objects)
        {
            var columns = await ReadColumnsAsync(connection, obj.Name, cancellationToken);
            columnCount += columns.Count;
            EnforceLimit(columnCount, MaxColumns, "SQLite schema column limit exceeded.");

            var foreignKeys = obj.Kind == "table"
                ? await ReadForeignKeysAsync(connection, obj.Name, cancellationToken)
                : [];
            foreignKeyCount += foreignKeys.Count;
            EnforceLimit(foreignKeyCount, MaxForeignKeys, "SQLite schema foreign-key limit exceeded.");

            var indexes = obj.Kind == "table"
                ? await ReadIndexesAsync(connection, obj.Name, cancellationToken)
                : [];
            indexCount += indexes.Count;
            EnforceLimit(indexCount, MaxIndexes, "SQLite schema index limit exceeded.");

            tables.Add(new DbTable(
                obj.Name,
                obj.Kind,
                columns,
                foreignKeys,
                indexes,
                Metadata: new Dictionary<string, string?>
                {
                    ["sqlite:sql"] = obj.Sql
                }));
        }

        var model = new DbSchemaModel(
            connectionName,
            "sqlite",
            tables,
            sourcePath ?? builder.DataSource,
            sessionId,
            new Dictionary<string, string?>
            {
                ["sqlite:version"] = sqliteVersion,
                ["objectCount"] = tables.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)
            });

        logger.LogInformation(
            "Inspected SQLite schema source={Source} objects={ObjectCount} columns={ColumnCount} fks={ForeignKeyCount} indexes={IndexCount}",
            SafeSourceLabel(model.SourcePath),
            tables.Count,
            columnCount,
            foreignKeyCount,
            indexCount);

        return model;
    }

    private static async Task<List<SchemaObject>> ReadSchemaObjectsAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        using var command = CreateCommand(connection, """
            SELECT name, type, sql
            FROM sqlite_schema
            WHERE type IN ('table', 'view')
              AND name NOT LIKE 'sqlite_%'
            ORDER BY type, name;
            """);
        using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var objects = new List<SchemaObject>();
        while (await reader.ReadAsync(cancellationToken))
        {
            EnforceLimit(objects.Count + 1, MaxSchemaObjects, "SQLite schema object limit exceeded.");
            objects.Add(new SchemaObject(
                reader.GetString(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2)));
        }

        return objects;
    }

    private static async Task<List<DbColumn>> ReadColumnsAsync(
        SqliteConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        using var command = CreateCommand(connection, $"PRAGMA table_info({QuoteIdentifier(tableName)});");
        using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var columns = new List<DbColumn>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var ordinal = reader.GetInt32(0);
            var name = reader.GetString(1);
            var storeType = reader.IsDBNull(2) ? null : EmptyToNull(reader.GetString(2));
            var notNull = reader.GetInt32(3) != 0;
            var defaultValue = reader.IsDBNull(4) ? null : reader.GetString(4);
            var primaryKeyOrdinal = reader.GetInt32(5);

            columns.Add(new DbColumn(
                name,
                storeType,
                IsNullable: !notNull && primaryKeyOrdinal == 0,
                PrimaryKeyOrdinal: primaryKeyOrdinal,
                DefaultValue: defaultValue,
                Ordinal: ordinal));
        }

        return columns;
    }

    private static async Task<List<DbForeignKey>> ReadForeignKeysAsync(
        SqliteConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        using var command = CreateCommand(connection, $"PRAGMA foreign_key_list({QuoteIdentifier(tableName)});");
        using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var rows = new List<ForeignKeyRow>();
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new ForeignKeyRow(
                Id: reader.GetInt32(0),
                Sequence: reader.GetInt32(1),
                PrincipalTable: reader.GetString(2),
                Column: reader.GetString(3),
                PrincipalColumn: reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                OnUpdate: reader.IsDBNull(5) ? null : reader.GetString(5),
                OnDelete: reader.IsDBNull(6) ? null : reader.GetString(6),
                Match: reader.IsDBNull(7) ? null : reader.GetString(7)));
        }

        return rows
            .GroupBy(row => row.Id)
            .OrderBy(group => group.Key)
            .Select(group =>
            {
                var ordered = group.OrderBy(row => row.Sequence).ToList();
                var first = ordered[0];
                return new DbForeignKey(
                    Name: $"fk_{tableName}_{group.Key}",
                    Table: tableName,
                    Columns: ordered.Select(row => row.Column).ToArray(),
                    PrincipalTable: first.PrincipalTable,
                    PrincipalColumns: ordered.Select(row => row.PrincipalColumn).ToArray(),
                    OnUpdate: first.OnUpdate,
                    OnDelete: first.OnDelete,
                    Match: first.Match,
                    Metadata: new Dictionary<string, string?>
                    {
                        ["sqlite:id"] = group.Key.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    });
            })
            .ToList();
    }

    private static async Task<List<DbIndex>> ReadIndexesAsync(
        SqliteConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        using var command = CreateCommand(connection, $"PRAGMA index_list({QuoteIdentifier(tableName)});");
        using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var refs = new List<IndexRef>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var name = reader.GetString(1);
            if (name.StartsWith("sqlite_autoindex_", StringComparison.OrdinalIgnoreCase))
                continue;

            refs.Add(new IndexRef(
                Name: name,
                IsUnique: reader.GetInt32(2) != 0,
                Origin: reader.IsDBNull(3) ? null : reader.GetString(3),
                IsPartial: !reader.IsDBNull(4) && reader.GetInt32(4) != 0));
        }

        var indexes = new List<DbIndex>(refs.Count);
        foreach (var index in refs)
        {
            var columns = await ReadIndexColumnsAsync(connection, index.Name, cancellationToken);
            if (columns.Count == 0)
                continue;

            indexes.Add(new DbIndex(
                index.Name,
                tableName,
                columns,
                index.IsUnique,
                index.IsPartial,
                index.Origin));
        }

        return indexes;
    }

    private static async Task<List<string>> ReadIndexColumnsAsync(
        SqliteConnection connection,
        string indexName,
        CancellationToken cancellationToken)
    {
        using var command = CreateCommand(connection, $"PRAGMA index_info({QuoteIdentifier(indexName)});");
        using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var columns = new List<(int Sequence, string Name)>();
        while (await reader.ReadAsync(cancellationToken))
        {
            if (reader.IsDBNull(2))
                continue;

            columns.Add((reader.GetInt32(0), reader.GetString(2)));
        }

        return columns
            .OrderBy(column => column.Sequence)
            .Select(column => column.Name)
            .ToList();
    }

    private static async Task ExecuteNonQueryAsync(
        SqliteConnection connection,
        string commandText,
        CancellationToken cancellationToken)
    {
        using var command = CreateCommand(connection, commandText);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<string?> ExecuteScalarStringAsync(
        SqliteConnection connection,
        string commandText,
        CancellationToken cancellationToken)
    {
        using var command = CreateCommand(connection, commandText);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value?.ToString();
    }

    private static SqliteCommand CreateCommand(SqliteConnection connection, string commandText)
    {
        var command = connection.CreateCommand();
        command.CommandText = commandText;
        command.CommandTimeout = CommandTimeoutSeconds;
        return command;
    }

    private static string QuoteIdentifier(string identifier) => "\"" + identifier.Replace("\"", "\"\"") + "\"";

    private static string? EmptyToNull(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static void EnforceLimit(int count, int limit, string message)
    {
        if (count > limit)
            throw new InvalidOperationException($"{message} Limit: {limit}.");
    }

    private static string SafeSourceLabel(string? sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
            return "sqlite";

        try
        {
            var fileName = Path.GetFileName(sourcePath);
            return string.IsNullOrWhiteSpace(fileName) ? "sqlite" : fileName;
        }
        catch (ArgumentException)
        {
            return "sqlite";
        }
    }

    private sealed record SchemaObject(string Name, string Kind, string? Sql);

    private sealed record ForeignKeyRow(
        int Id,
        int Sequence,
        string PrincipalTable,
        string Column,
        string PrincipalColumn,
        string? OnUpdate,
        string? OnDelete,
        string? Match);

    private sealed record IndexRef(string Name, bool IsUnique, string? Origin, bool IsPartial);
}
