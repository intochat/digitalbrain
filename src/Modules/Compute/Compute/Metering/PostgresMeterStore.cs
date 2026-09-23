using Npgsql;

namespace DigitalBrain.Compute.Metering;

// Append-only PostgreSQL meter table. The primary key is the idempotency key, so a
// duplicate insert is a no-op instead of a second event (T3, no Orleans transactions).
internal sealed class PostgresMeterStore(NpgsqlDataSource source) : IMeterStore
{
    private const string CreateTable = """
        CREATE TABLE IF NOT EXISTS compute_meter_event (
            intent_id    text        NOT NULL,
            meter_id     text        NOT NULL,
            step         text        NOT NULL,
            workspace_id text        NOT NULL,
            quantity     numeric(24,6) NOT NULL,
            unit         text        NOT NULL,
            source       integer     NOT NULL,
            app_id       text        NULL,
            cost_basis   text        NULL,
            occurred_at  timestamptz NOT NULL,
            CONSTRAINT compute_meter_event_pkey PRIMARY KEY (intent_id, meter_id, step)
        );
        """;

    private bool initialized;

    public async ValueTask<bool> AppendAsync(MeterEvent meterEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(meterEvent);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO compute_meter_event
                (intent_id, meter_id, step, workspace_id, quantity, unit, source, app_id, cost_basis, occurred_at)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10)
            ON CONFLICT (intent_id, meter_id, step) DO NOTHING;
            """;
        command.Parameters.Add(new NpgsqlParameter { Value = meterEvent.IntentId });
        command.Parameters.Add(new NpgsqlParameter { Value = meterEvent.MeterId });
        command.Parameters.Add(new NpgsqlParameter { Value = meterEvent.Step });
        command.Parameters.Add(new NpgsqlParameter { Value = meterEvent.WorkspaceId });
        command.Parameters.Add(new NpgsqlParameter { Value = meterEvent.Quantity });
        command.Parameters.Add(new NpgsqlParameter { Value = meterEvent.Unit });
        command.Parameters.Add(new NpgsqlParameter { Value = (int)meterEvent.Source });
        command.Parameters.Add(new NpgsqlParameter { Value = (object?)meterEvent.AppId ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter { Value = (object?)meterEvent.CostBasis ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter { Value = meterEvent.OccurredAt });
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
    }

    public async ValueTask<IReadOnlyList<MeterEvent>> ReadAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT intent_id, meter_id, step, workspace_id, quantity, unit, source, app_id, cost_basis, occurred_at
            FROM compute_meter_event ORDER BY occurred_at, intent_id, meter_id, step;
            """;
        var events = new List<MeterEvent>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            events.Add(new MeterEvent
            {
                IntentId = reader.GetString(0),
                MeterId = reader.GetString(1),
                Step = reader.GetString(2),
                WorkspaceId = reader.GetString(3),
                Quantity = reader.GetDecimal(4),
                Unit = reader.GetString(5),
                Source = (MeterSource)reader.GetInt32(6),
                AppId = reader.IsDBNull(7) ? null : reader.GetString(7),
                CostBasis = reader.IsDBNull(8) ? null : reader.GetString(8),
                OccurredAt = reader.GetFieldValue<DateTimeOffset>(9),
            });
        }
        return events;
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
