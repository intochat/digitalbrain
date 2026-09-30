using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace DigitalBrain.Postgres;

internal sealed class PostgresProvider([FromKeyedServices(PostgresHosting.DataSourceKey)] NpgsqlDataSource source) : IPostgresProvider
{
    public async Task<PostgresQueryResult> QueryAsync(string sql, int maxRows, CancellationToken cancellationToken)
    {
        sql = PostgresQueryGuard.Normalize(sql);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxRows, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxRows, PostgresQuery.MaxRowsLimit);
        var started = Stopwatch.GetTimestamp();
        var (columns, rows, _) = await ReadAsync($"SELECT * FROM ({sql}) AS q LIMIT $1", [maxRows + 1], maxRows + 1, cancellationToken).ConfigureAwait(false);
        var truncated = rows.Count > maxRows;
        var page = rows.Take(maxRows).ToArray();
        return new(columns, page, page.Length, truncated, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
    }

    public async Task<PostgresSchema> ReadSchemaAsync(string? table, CancellationToken cancellationToken)
    {
        // Discover application schemas visible to the connected role; keep internal schemas out of the index.
        // PostgreSQL parses quoted identifiers so dots/case inside quotes retain their normal SQL meaning.
        var (_, rows, database) = await ReadAsync("""
            WITH requested AS (SELECT parse_ident($1::text) AS parts)
            SELECT format('%I.%I', c.table_schema, c.table_name), c.column_name, c.udt_name, t.table_type
            FROM information_schema.columns c
            JOIN information_schema.tables t ON t.table_schema = c.table_schema AND t.table_name = c.table_name
            CROSS JOIN requested r
            WHERE c.table_schema <> 'information_schema'
                AND left(c.table_schema, 3) <> 'pg_'
                AND has_schema_privilege(c.table_schema, 'USAGE')
                AND ($1::text IS NULL OR CASE cardinality(r.parts)
                    WHEN 1 THEN c.table_name = r.parts[1]
                    WHEN 2 THEN c.table_schema = r.parts[1] AND c.table_name = r.parts[2]
                    WHEN 3 THEN current_database() = r.parts[1] AND c.table_schema = r.parts[2] AND c.table_name = r.parts[3]
                    ELSE false END)
            ORDER BY c.table_schema, c.table_name, c.ordinal_position
            LIMIT 10001
            """, [table is null ? DBNull.Value : table], 10001, cancellationToken).ConfigureAwait(false);
        if (rows.Count > 10000) { throw new PostgresQueryException("Schema exceeds 10000 columns. Request a specific table."); }
        // Row cells are JSON literals for the table wire contract; schema metadata
        // needs the decoded strings before grouping, type mapping or model use.
        var metadata = rows.Select(row => row.Select(cell => JsonSerializer.Deserialize<string>(cell)!).ToArray());
        var tables = metadata.GroupBy(row => row[0], StringComparer.Ordinal)
            .Select(group => new PostgresTableInfo(group.Key, group.First()[3], null,
                group.Select(row => new PostgresColumn(row[1], row[2], PostgresTypeMap.ToTableType(row[2]))).ToArray()))
            .ToArray();
        return new(database, tables);
    }

    public async Task<PostgresConnection> PingAsync(CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            var (_, rows, database) = await ReadAsync("SELECT version()", [], 1, deadline.Token).ConfigureAwait(false);
            return new(true, database, JsonSerializer.Deserialize<string>(rows[0][0]), PostgresModule.ProviderName);
        }
        catch (Exception error) when (error is PostgresUnavailableException or PostgresQueryException ||
            (error is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            // With no live connection, report only the explicitly configured database.
            var database = new NpgsqlConnectionStringBuilder(source.ConnectionString).Database ?? string.Empty;
            return new(false, database, null, PostgresModule.ProviderName);
        }
    }

    private async Task<(PostgresColumn[] Columns, List<string[]> Rows, string Database)> ReadAsync(
        string sql, IReadOnlyList<object> parameters, int maxRows, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            await using var connection = await source.OpenConnectionAsync(deadline.Token).ConfigureAwait(false);
            await using var transaction = await connection.BeginTransactionAsync(deadline.Token).ConfigureAwait(false);
            // Every statement, including schema, counts and descriptions, uses the same server-side protection.
            await using (var setup = new NpgsqlCommand("SET TRANSACTION READ ONLY; SET LOCAL statement_timeout = '15s'; SET LOCAL lock_timeout = '3s'; SET LOCAL standard_conforming_strings = on; SET LOCAL timezone = 'UTC'; SET LOCAL datestyle = 'ISO, YMD'", connection, transaction))
            {
                await setup.ExecuteNonQueryAsync(deadline.Token).ConfigureAwait(false);
            }
            await using var command = new NpgsqlCommand(sql, connection, transaction) { CommandTimeout = 15, AllResultTypesAreUnknown = true };
            foreach (var value in parameters) { command.Parameters.Add(new NpgsqlParameter { Value = value }); }
            await using var reader = await command.ExecuteReaderAsync(deadline.Token).ConfigureAwait(false);
            var columns = Enumerable.Range(0, reader.FieldCount)
                .Select(index => new PostgresColumn(reader.GetName(index), reader.GetDataTypeName(index), PostgresTypeMap.ToTableType(reader.GetDataTypeName(index))))
                .ToArray();
            var rows = new List<string[]>();
            while (rows.Count < maxRows && await reader.ReadAsync(deadline.Token).ConfigureAwait(false))
            {
                var cells = new string[columns.Length];
                for (var index = 0; index < cells.Length; index++)
                {
                    cells[index] = PostgresCells.ToCell(reader.IsDBNull(index) ? null : reader.GetString(index), columns[index].TableType);
                }
                rows.Add(cells);
            }
            // Disposal rolls back the read-only transaction before returning the connection to the pool.
            return (columns, rows, connection.Database);
        }
        catch (PostgresException error)
        {
            // SQLSTATE is useful for correction without exposing server details, query data or credentials.
            throw new PostgresQueryException($"PostgreSQL refused the query (SQLSTATE {error.SqlState}). Check table/column names, permissions and read-only SQL; narrow expensive queries.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new PostgresQueryException("The query timed out. Narrow it with WHERE or LIMIT.");
        }
        catch (NpgsqlException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new PostgresUnavailableException("Postgres is unreachable or the database connection failed. Check the configured connection string and network.");
        }
    }
}
