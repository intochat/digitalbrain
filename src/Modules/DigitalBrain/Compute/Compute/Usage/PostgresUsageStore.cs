using Npgsql;

namespace DigitalBrain.Compute.Usage;

internal sealed class PostgresUsageStore(ComputeDatabase database, bool readOnly = false) : IUsageStore
{
    private bool initialized;
    private readonly SemaphoreSlim schemaGate = new(1);
    public async ValueTask EnsureCreatedAsync(CancellationToken ct = default)
    {
        await using var connection = await Open(ct).ConfigureAwait(false);
    }

    private async Task<NpgsqlConnection> Open(CancellationToken ct)
    {
        var connection = await database.Source.OpenConnectionAsync(ct).ConfigureAwait(false);
        if (readOnly) { return connection; }
        try
        {
            await schemaGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (!initialized)
                {
                    await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
                    await using var command = connection.CreateCommand();
                    command.CommandText = """
                        SELECT pg_advisory_xact_lock(736219483501);
                        CREATE TABLE IF NOT EXISTS compute_usage (
                            account_id text NOT NULL, workspace_id text NOT NULL, id text NOT NULL,
                            sort_key text COLLATE "C" NOT NULL, payload text NOT NULL,
                            PRIMARY KEY (account_id, workspace_id, id));
                        CREATE INDEX IF NOT EXISTS compute_usage_page ON compute_usage(account_id, workspace_id, sort_key DESC);
                        ALTER TABLE compute_usage ADD COLUMN IF NOT EXISTS revision bigint NOT NULL DEFAULT 0;
                        """;
                    await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                    await transaction.CommitAsync(ct).ConfigureAwait(false);
                    initialized = true;
                }
            }
            finally { schemaGate.Release(); }
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
    public async ValueTask AppendAsync(string account, string workspace, string id, string payload, CancellationToken ct = default, DateTimeOffset? revision = null)
    {
        if (readOnly) { throw new InvalidOperationException("Legacy compute storage is read-only."); }
        await using var connection = await Open(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO compute_usage (account_id,workspace_id,id,sort_key,payload,revision) VALUES ($1,$2,$3,$4,$5,$6)
            ON CONFLICT (account_id,workspace_id,id) DO UPDATE SET payload=EXCLUDED.payload, revision=EXCLUDED.revision
            WHERE compute_usage.revision <= EXCLUDED.revision
            """;
        foreach (var value in new[] { account, workspace, id, UsagePaging.Key(id), payload }) { command.Parameters.Add(new NpgsqlParameter { Value = value }); }
        command.Parameters.Add(new NpgsqlParameter { Value = (revision ?? DateTimeOffset.UtcNow).UtcTicks });
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
    public async ValueTask<UsagePage> ReadAsync(string account, string workspace, int limit, string? cursor, CancellationToken ct = default)
    {
        var before = UsagePaging.Decode(account, workspace, limit, cursor);
        await using var connection = await Open(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id,payload,sort_key,revision FROM compute_usage WHERE account_id=$1 AND workspace_id=$2 AND ($3::text IS NULL OR sort_key < $3) ORDER BY sort_key DESC LIMIT $4";
        command.Parameters.Add(new NpgsqlParameter { Value = account });
        command.Parameters.Add(new NpgsqlParameter { Value = workspace });
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Text, Value = (object?)before ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter { Value = limit + 1 });
        var rows = new List<UsageRow>();
        string? last = null;
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            if (rows.Count == limit) { return new(rows, UsagePaging.Encode(account, workspace, last!)); }
            rows.Add(new(reader.GetString(0), reader.GetString(1), UsagePaging.OccurredAt(reader.GetString(2)), reader.GetInt64(3)));
            last = reader.GetString(2);
        }
        return new(rows, null);
    }
}
