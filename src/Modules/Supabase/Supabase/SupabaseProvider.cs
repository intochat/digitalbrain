using System.Diagnostics;
using System.Text.Json;
using DigitalBrain.Flutter;
using Npgsql;

namespace DigitalBrain.Supabase;

internal sealed class SupabaseProvider(NpgsqlDataSource source) : ISupabaseProvider
{
    public string ProviderName => SupabaseModule.ProviderName;

    public async Task<SupabaseQueryResult> QueryAsync(string sql, int maxRows, CancellationToken cancellationToken)
    {
        SupabaseQueryGuard.Validate(sql);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxRows, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxRows, SupabaseQuery.MaxRowsLimit);
        var started = Stopwatch.GetTimestamp();
        var (columns, rows) = await ReadAsync($"SELECT * FROM ({sql}) AS q LIMIT $1", [maxRows + 1], maxRows + 1, cancellationToken).ConfigureAwait(false);
        var truncated = rows.Count > maxRows;
        var page = rows.Take(maxRows).ToArray();
        return new(columns, page, page.Length, truncated, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
    }

    public async Task<IReadOnlyList<SupabaseColumn>> DescribeAsync(string sql, CancellationToken cancellationToken)
    {
        SupabaseQueryGuard.Validate(sql);
        var (columns, _) = await ReadAsync($"SELECT * FROM ({sql}) AS q LIMIT 0", [], 0, cancellationToken).ConfigureAwait(false);
        return columns;
    }

    public async Task<QueryPage> ExecutePlanAsync(QueryPlan plan, CancellationToken cancellationToken)
    {
        SupabaseQueryGuard.Validate(plan.BaseSql);
        var compiled = QueryPlanCompiler.Compile(plan);
        var (_, rows) = await ReadAsync(compiled.PageSql, compiled.Parameters, plan.Limit, cancellationToken).ConfigureAwait(false);
        var filtered = await CountAsync(compiled.FilteredCountSql, compiled.Parameters, cancellationToken).ConfigureAwait(false);
        var total = plan.Filters.Count == 0 ? filtered : await CountAsync(compiled.TotalCountSql, [], cancellationToken).ConfigureAwait(false);
        return new(rows.Select((cells, index) => new TableRow($"row-{plan.Offset + index}", cells)).ToArray(), total, filtered);
    }

    public async Task<SupabaseSchema> ReadSchemaAsync(string? table, CancellationToken cancellationToken)
    {
        // Discover application schemas visible to the connected role; keep internal schemas out of the index.
        // PostgreSQL parses quoted identifiers so dots/case inside quotes retain their normal SQL meaning.
        var (_, rows) = await ReadAsync("""
            WITH requested AS (SELECT parse_ident($1::text) AS parts)
            SELECT format('%I.%I', c.table_schema, c.table_name), c.column_name, c.udt_name, t.table_type
            FROM information_schema.columns c
            JOIN information_schema.tables t ON t.table_schema = c.table_schema AND t.table_name = c.table_name
            CROSS JOIN requested r
            WHERE c.table_schema NOT IN ('information_schema', 'auth', 'storage', 'extensions', 'realtime',
                'supabase_functions', 'supabase_migrations', 'vault', 'graphql', 'graphql_public', 'pgbouncer', 'net', 'cron')
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
        if (rows.Count > 10000) { throw new SupabaseQueryException("Schema exceeds 10000 columns. Request a specific table."); }
        var tables = rows.GroupBy(row => row[0].GetString()!, StringComparer.Ordinal)
            .Select(group => new SupabaseTableInfo(group.Key, group.First()[3].GetString()!, null,
                group.Select(row => new SupabaseColumn(row[1].GetString()!, row[2].GetString()!, SupabaseTypeMap.ToTableType(row[2].GetString()!))).ToArray()))
            .ToArray();
        return new(Database, tables);
    }

    public async Task<SupabaseConnection> PingAsync(CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            var (_, rows) = await ReadAsync("SELECT version()", [], 1, deadline.Token).ConfigureAwait(false);
            return new(true, Database, rows[0][0].GetString(), ProviderName);
        }
        catch (Exception error) when (error is SupabaseUnavailableException or SupabaseQueryException ||
            (error is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return new(false, Database, null, ProviderName);
        }
    }

    private string Database => new NpgsqlConnectionStringBuilder(source.ConnectionString).Database ?? "postgres";

    private async Task<long> CountAsync(string sql, IReadOnlyList<object> parameters, CancellationToken cancellationToken)
    {
        var (_, rows) = await ReadAsync(sql, parameters, 1, cancellationToken).ConfigureAwait(false);
        return long.Parse(rows[0][0].ToString(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private async Task<(SupabaseColumn[] Columns, List<JsonElement[]> Rows)> ReadAsync(
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
                .Select(index => new SupabaseColumn(reader.GetName(index), reader.GetDataTypeName(index), SupabaseTypeMap.ToTableType(reader.GetDataTypeName(index))))
                .ToArray();
            var rows = new List<JsonElement[]>();
            while (rows.Count < maxRows && await reader.ReadAsync(deadline.Token).ConfigureAwait(false))
            {
                var cells = new JsonElement[columns.Length];
                for (var index = 0; index < cells.Length; index++)
                {
                    cells[index] = SupabaseCells.ToCell(reader.IsDBNull(index) ? null : reader.GetString(index), columns[index].TableType);
                }
                rows.Add(cells);
            }
            // Disposal rolls back the read-only transaction before returning the connection to the pool.
            return (columns, rows);
        }
        catch (PostgresException error)
        {
            // SQLSTATE is useful for correction without exposing server details, query data or credentials.
            throw new SupabaseQueryException($"PostgreSQL refused the query (SQLSTATE {error.SqlState}). Check table/column names, permissions and read-only SQL; narrow expensive queries.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new SupabaseQueryException("The query timed out. Narrow it with WHERE or LIMIT.");
        }
        catch (NpgsqlException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new SupabaseUnavailableException("Supabase is unreachable or the database connection failed. Check the configured connection string and network.");
        }
    }
}
