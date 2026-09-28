using System.Globalization;
using System.Text.Json;
using DigitalBrain.Compute.Ledger;
using DigitalBrain.Compute.Metering;
using DigitalBrain.Compute.Usage;

namespace DigitalBrain.Compute.Storage;

// These sources are optional, read-only migration adapters. Once a scope is
// imported, a persisted marker makes it independent of the legacy host/database.
internal sealed record LegacyComputeSources(ILedgerStore? Ledger = null, IMeterStore? Meter = null, IUsageStore? Usage = null);

internal sealed class NeuronLedgerStore(IGrainFactory grains, LegacyComputeSources legacy) : ILedgerStore
{
    private IComputeRecords Records(string account) => grains.GetGrain<IComputeRecords>("ledger/" + UsagePaging.Hash(account));
    private static ComputeStoredRecord Record(LedgerEntry entry) => new(entry.IdempotencyKey, JsonSerializer.Serialize(entry),
        entry.OccurredAt.UtcTicks.ToString("D19", CultureInfo.InvariantCulture) + "-" + UsagePaging.Hash(entry.IdempotencyKey), 0);

    private async Task<IComputeRecords> Ready(string account, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(account);
        ct.ThrowIfCancellationRequested();
        var records = Records(account);
        if (legacy.Ledger is not null && !await records.IsImported())
        {
            var rows = await legacy.Ledger.ReadAsync(account, ct);
            foreach (var batch in rows.Chunk(100)) { await records.Put(batch.Select(Record).ToArray(), false); }
            await records.CompleteImport();
        }
        return records;
    }

    public async ValueTask<LedgerAppend> AppendAsync(LedgerEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var records = await Ready(entry.AccountId, cancellationToken);
        return await records.Put([Record(entry)], false) == 1 ? LedgerAppend.Inserted : LedgerAppend.Duplicate;
    }

    public async ValueTask<IReadOnlyList<LedgerEntry>> ReadAsync(string accountId, CancellationToken cancellationToken = default)
    {
        var records = await Ready(accountId, cancellationToken);
        return (await records.Read()).Select(row => JsonSerializer.Deserialize<LedgerEntry>(row.Payload)!).ToArray();
    }

    public async ValueTask<decimal> ChargedAsync(string accountId, string? intentId, CancellationToken cancellationToken = default)
        => (await ReadAsync(accountId, cancellationToken)).Where(entry => entry.Kind == LedgerKind.WalletCharge
            && (intentId is null || entry.IntentId == intentId)).Sum(entry => entry.Amount);
}

internal sealed class NeuronMeterStore(IGrainFactory grains, LegacyComputeSources legacy) : IMeterStore
{
    private IComputeRecords Records => grains.GetGrain<IComputeRecords>("meters/v1");
    private static ComputeStoredRecord Record(MeterEvent value)
    {
        var id = JsonSerializer.Serialize(new[] { value.IntentId, value.MeterId, value.Step });
        return new(id, JsonSerializer.Serialize(value), value.OccurredAt.UtcTicks.ToString("D19", CultureInfo.InvariantCulture) + "-" + UsagePaging.Hash(id), 0);
    }

    private async Task<IComputeRecords> Ready(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var records = Records;
        if (legacy.Meter is not null && !await records.IsImported())
        {
            var rows = await legacy.Meter.ReadAsync(ct);
            foreach (var batch in rows.Chunk(100)) { await records.Put(batch.Select(Record).ToArray(), false); }
            await records.CompleteImport();
        }
        return records;
    }

    public async ValueTask<bool> AppendAsync(MeterEvent meterEvent, CancellationToken cancellationToken = default)
        => await AppendBatchAsync([meterEvent], cancellationToken) == 1;

    public async ValueTask<int> AppendBatchAsync(IReadOnlyList<MeterEvent> meterEvents, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(meterEvents);
        var records = await Ready(cancellationToken);
        // Existing intent batches commit with one root update, including dedup.
        return await records.Put(meterEvents.Select(Record).ToArray(), false);
    }

    public async ValueTask<IReadOnlyList<MeterEvent>> ReadAsync(CancellationToken cancellationToken = default)
    {
        var records = await Ready(cancellationToken);
        return (await records.Read()).Select(row => JsonSerializer.Deserialize<MeterEvent>(row.Payload)!).ToArray();
    }

    public ValueTask EnsureCreatedAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }
}

internal sealed class NeuronUsageStore(IGrainFactory grains, LegacyComputeSources legacy) : IUsageStore
{
    private IComputeRecords Records(string account, string workspace) => grains.GetGrain<IComputeRecords>("usage/" + UsagePaging.Scope(account, workspace));

    private async Task<IComputeRecords> Ready(string account, string workspace, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(account);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspace);
        ct.ThrowIfCancellationRequested();
        var records = Records(account, workspace);
        if (legacy.Usage is not null && !await records.IsImported())
        {
            string? cursor = null;
            do
            {
                var page = await legacy.Usage.ReadAsync(account, workspace, 100, cursor, ct);
                await records.Put(page.Items.Select(row => new ComputeStoredRecord(row.Id, row.Payload,
                    row.OccurredAt.UtcTicks.ToString("D19", CultureInfo.InvariantCulture) + "-" + UsagePaging.Hash(row.Id),
                    row.Revision ?? 0)).ToArray(), true);
                cursor = page.NextCursor;
            } while (cursor is not null);
            await records.CompleteImport();
        }
        return records;
    }

    public async ValueTask AppendAsync(string account, string workspace, string id, string payload, CancellationToken ct = default, DateTimeOffset? revision = null)
    {
        var records = await Ready(account, workspace, ct);
        await records.Put([new(id, payload, UsagePaging.Key(id), (revision ?? DateTimeOffset.UtcNow).UtcTicks)], true);
    }

    public async ValueTask<UsagePage> ReadAsync(string account, string workspace, int limit, string? cursor, CancellationToken ct = default)
    {
        var before = UsagePaging.Decode(account, workspace, limit, cursor);
        var records = await Ready(account, workspace, ct);
        var page = await records.Page(before, limit);
        return new(page.Items.Select(row => new UsageRow(row.Id, row.Payload, UsagePaging.OccurredAt(row.SortKey), row.Revision)).ToArray(),
            page.NextKey is null ? null : UsagePaging.Encode(account, workspace, page.NextKey));
    }
}
