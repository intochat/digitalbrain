using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NpgsqlTypes;

namespace DigitalBrain.Postgres;

internal sealed class PostgresTableProvider(IPostgresSourceRegistry registry) : IPostgresTableProvider
{
    private static string Q(string identifier)
    {
        PostgresTablePolicy.Identifier(identifier);
        return "\"" + identifier + "\"";
    }

    private static string Table(string table) => "public." + Q(table);
    private static string Parameter(TableDefinition definition, TableValue value, int index)
    {
        var type = PostgresTablePolicy.SqlType(definition.Columns.Single(c => c.Name == value.Column).Type);
        return type == "jsonb" ? $"${index}::jsonb" : $"(${index}::jsonb #>> '{{}}')::{type}";
    }
    private static string Predicate(TableDefinition definition, TableValue[] key)
        => string.Join(" AND ", key.Select((v, i) => $"{Q(v.Column)} = {Parameter(definition, v, i + 1)}"));

    public Task DefineAsync(string origin, string table, TableDefinition definition, CancellationToken ct)
        => Execute(origin, async (connection, transaction, token) =>
        {
            definition = PostgresTablePolicy.Validate(definition);
            await using (var guard = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended($1, 0))", connection, transaction))
            {
                guard.Parameters.AddWithValue(table);
                await guard.ExecuteNonQueryAsync(token);
            }
            var columns = string.Join(",", definition.Columns.Select(c => $"{Q(c.Name)} {PostgresTablePolicy.SqlType(c.Type)}"));
            await using (var create = new NpgsqlCommand($"CREATE TABLE IF NOT EXISTS {Table(table)} ({columns}, PRIMARY KEY ({string.Join(",", definition.PrimaryKey.Select(Q))}))", connection, transaction))
            { await create.ExecuteNonQueryAsync(token); }
            await using var inspect = new NpgsqlCommand("""
                SELECT a.attname, t.typname, a.attnotnull,
                    EXISTS (SELECT 1 FROM pg_constraint k WHERE k.conrelid=c.oid AND k.contype='p' AND a.attnum=ANY(k.conkey))
                FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
                JOIN pg_attribute a ON a.attrelid=c.oid JOIN pg_type t ON t.oid=a.atttypid
                WHERE n.nspname='public' AND c.relname=$1 AND c.relkind='r' AND a.attnum>0 AND NOT a.attisdropped
                ORDER BY a.attname COLLATE "C"
                """, connection, transaction);
            inspect.Parameters.AddWithValue(table);
            await using var reader = await inspect.ExecuteReaderAsync(token);
            var actual = new List<TableColumn>();
            var keys = new List<string>();
            while (await reader.ReadAsync(token))
            {
                var name = reader.GetString(0);
                var type = reader.GetString(1) switch { "numeric" => "number", "bool" => "boolean", var other => other };
                actual.Add(new(name, type));
                if (reader.GetBoolean(3)) { keys.Add(name); }
                if (reader.GetBoolean(2) != reader.GetBoolean(3))
                { throw new PostgresQueryException("Incompatible existing table nullability; migrations are not supported."); }
            }
            if (!PostgresTablePolicy.Compatible(definition, new(actual.ToArray(), keys.ToArray())))
            { throw new PostgresQueryException("Incompatible existing table schema; migrations are not supported."); }
            return true;
        }, ct);

    public Task<bool> UpsertAsync(string origin, string table, TableDefinition definition, TableValue[] key, TableValue[] values, CancellationToken ct)
    {
        var all = key.Concat(values).ToArray();
        var conflict = values.Length == 0 ? "DO NOTHING" :
            $"DO UPDATE SET {string.Join(",", values.Select(v => $"{Q(v.Column)}=EXCLUDED.{Q(v.Column)}"))} WHERE " +
            string.Join(" OR ", values.Select(v => $"target.{Q(v.Column)} IS DISTINCT FROM EXCLUDED.{Q(v.Column)}"));
        var sql = $"INSERT INTO {Table(table)} AS target ({string.Join(",", all.Select(v => Q(v.Column)))}) VALUES ({string.Join(",", all.Select((v, i) => Parameter(definition, v, i + 1)))}) ON CONFLICT ({string.Join(",", definition.PrimaryKey.Select(Q))}) {conflict}";
        return Change(origin, sql, all, ct);
    }

    public Task<bool> DeleteAsync(string origin, string table, TableDefinition definition, TableValue[] key, CancellationToken ct)
        => Change(origin, $"DELETE FROM {Table(table)} WHERE {Predicate(definition, key)}", key, ct);

    public async Task<TableValue[]?> ReadAsync(string origin, string table, TableDefinition definition, TableValue[] key, CancellationToken ct)
        => (await Rows(origin, $"SELECT to_jsonb(t)::text FROM {Table(table)} t WHERE {Predicate(definition, key)}", key.Select(v => (object)v.Json).ToArray(), ct)).SingleOrDefault();

    public Task<TableValue[][]> PageAsync(string origin, string table, TableDefinition definition, int offset, int limit, CancellationToken ct)
        => Rows(origin, $"SELECT to_jsonb(t)::text FROM {Table(table)} t ORDER BY {string.Join(",", definition.PrimaryKey.Select(Q))} OFFSET $1 LIMIT $2", [offset, limit], ct);

    private Task<bool> Change(string origin, string sql, TableValue[] values, CancellationToken ct)
        => Execute(origin, async (connection, transaction, token) =>
        {
            await using var command = Command(sql, values.Select(v => (object)v.Json).ToArray(), connection, transaction);
            return await command.ExecuteNonQueryAsync(token) > 0;
        }, ct);

    private Task<TableValue[][]> Rows(string origin, string sql, object[] values, CancellationToken ct)
        => Execute(origin, async (connection, transaction, token) =>
        {
            await using var command = Command(sql, values, connection, transaction);
            await using var reader = await command.ExecuteReaderAsync(token);
            var rows = new List<TableValue[]>();
            while (await reader.ReadAsync(token))
            {
                using var document = JsonDocument.Parse(reader.GetString(0));
                rows.Add(document.RootElement.EnumerateObject().Select(p => new TableValue(p.Name, p.Value.GetRawText())).ToArray());
            }
            return rows.ToArray();
        }, ct);

    private static NpgsqlCommand Command(string sql, object[] values, NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        var command = new NpgsqlCommand(sql, connection, transaction) { CommandTimeout = 15 };
        foreach (var value in values) { command.Parameters.Add(new NpgsqlParameter { Value = value, NpgsqlDbType = value is int ? NpgsqlDbType.Integer : NpgsqlDbType.Text }); }
        return command;
    }

    private async Task<T> Execute<T>(string origin, Func<NpgsqlConnection, NpgsqlTransaction, CancellationToken, Task<T>> action, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            await using var connection = await registry.Get(origin).OpenConnectionAsync(deadline.Token);
            await using var transaction = await connection.BeginTransactionAsync(deadline.Token);
            await using (var setup = new NpgsqlCommand("SET LOCAL statement_timeout='15s'; SET LOCAL lock_timeout='3s'; SET LOCAL timezone='UTC'", connection, transaction))
            { await setup.ExecuteNonQueryAsync(deadline.Token); }
            var result = await action(connection, transaction, deadline.Token);
            await transaction.CommitAsync(deadline.Token);
            return result;
        }
        catch (PostgresException error) { throw new PostgresQueryException($"PostgreSQL refused the table operation (SQLSTATE {error.SqlState})."); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new PostgresQueryException("The table operation timed out."); }
        catch (NpgsqlException) { throw new PostgresUnavailableException("Postgres is unreachable or the database connection failed."); }
    }
}
