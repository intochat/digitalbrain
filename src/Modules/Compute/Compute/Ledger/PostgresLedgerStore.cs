using Npgsql;

namespace DigitalBrain.Compute.Ledger;

// Append-only PostgreSQL ledger. The UNIQUE constraint on (account_id, idempotency_key)
// is the single defence against a double charge; the writer never reads before writing.
internal sealed class PostgresLedgerStore(NpgsqlDataSource source) : ILedgerStore
{
    private const string CreateTable = """
        CREATE TABLE IF NOT EXISTS compute_ledger_entry (
            account_id      text        NOT NULL,
            idempotency_key text        NOT NULL,
            kind            integer     NOT NULL,
            amount          numeric(24,6) NOT NULL,
            intent_id       text        NULL,
            app_id          text        NULL,
            description     text        NULL,
            occurred_at     timestamptz NOT NULL,
            CONSTRAINT compute_ledger_entry_pkey PRIMARY KEY (account_id, idempotency_key)
        );
        """;

    private bool initialized;

    public async ValueTask<LedgerAppend> AppendAsync(LedgerEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO compute_ledger_entry
                (account_id, idempotency_key, kind, amount, intent_id, app_id, description, occurred_at)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8)
            ON CONFLICT (account_id, idempotency_key) DO NOTHING;
            """;
        command.Parameters.Add(new NpgsqlParameter { Value = entry.AccountId });
        command.Parameters.Add(new NpgsqlParameter { Value = entry.IdempotencyKey });
        command.Parameters.Add(new NpgsqlParameter { Value = (int)entry.Kind });
        command.Parameters.Add(new NpgsqlParameter { Value = entry.Amount });
        command.Parameters.Add(new NpgsqlParameter { Value = (object?)entry.IntentId ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter { Value = (object?)entry.AppId ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter { Value = (object?)entry.Description ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter { Value = entry.OccurredAt });
        var inserted = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
        return inserted ? LedgerAppend.Inserted : LedgerAppend.Duplicate;
    }

    public async ValueTask<IReadOnlyList<LedgerEntry>> ReadAsync(string accountId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT account_id, idempotency_key, kind, amount, intent_id, app_id, description, occurred_at
            FROM compute_ledger_entry WHERE account_id = $1 ORDER BY occurred_at, idempotency_key;
            """;
        command.Parameters.Add(new NpgsqlParameter { Value = accountId });
        var entries = new List<LedgerEntry>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            entries.Add(new LedgerEntry
            {
                AccountId = reader.GetString(0),
                IdempotencyKey = reader.GetString(1),
                Kind = (LedgerKind)reader.GetInt32(2),
                Amount = reader.GetDecimal(3),
                IntentId = reader.IsDBNull(4) ? null : reader.GetString(4),
                AppId = reader.IsDBNull(5) ? null : reader.GetString(5),
                Description = reader.IsDBNull(6) ? null : reader.GetString(6),
                OccurredAt = reader.GetFieldValue<DateTimeOffset>(7),
            });
        }
        return entries;
    }

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = await source.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        if (initialized) { return connection; }
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = CreateTable;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        initialized = true;
        return connection;
    }
}
