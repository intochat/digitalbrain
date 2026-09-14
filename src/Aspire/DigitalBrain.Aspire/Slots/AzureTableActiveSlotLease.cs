using Azure;
using Azure.Data.Tables;
using DigitalBrain.Abstractions.Slots;
using DigitalBrain.Core;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.Aspire;

// The lease every grain call consults. Reads are answered from the cache the refresher renews; a hand-over
// is one compare-and-swap on the row's ETag, which is what fences a silo that still believes it is live.
public sealed class AzureTableActiveSlotLease : IActiveSlotLease
{
    private readonly TableServiceClient _tables;
    private readonly TableClient _leaseTable;
    private readonly TimeProvider _clock;
    private readonly ILogger<AzureTableActiveSlotLease> _logger;
    private Cached _cached = new(false, 0);
    private Task? _tableReadyTask;

    public AzureTableActiveSlotLease(string slot, TableServiceClient tables, TimeProvider clock, ILogger<AzureTableActiveSlotLease> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slot);
        ArgumentNullException.ThrowIfNull(tables);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        Slot = slot;
        _tables = tables;
        _leaseTable = tables.GetTableClient(ActiveSlotNames.Table);
        _clock = clock;
        _logger = logger;
    }

    public string Slot { get; }

    public bool HoldsLease => Volatile.Read(ref _cached).Holds;

    public long Generation => Volatile.Read(ref _cached).Generation;

    // A slot that starts against an empty table takes the row; a second slot then reads an owner and stays
    // standby. Never a hand-over: that is TryAcquireAsync's job.
    public async Task BootstrapAsync(CancellationToken cancellationToken = default)
    {
        await EnsureTableAsync(cancellationToken).ConfigureAwait(false);
        if (await ReadAsync(cancellationToken).ConfigureAwait(false) is { } existing)
        {
            Cache(existing.Owner, existing.Generation);
            return;
        }

        await SeatOnMissingRowAsync(Slot, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> TryAcquireAsync(string slot, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slot);
        await EnsureTableAsync(cancellationToken).ConfigureAwait(false);
        if (await ReadAsync(cancellationToken).ConfigureAwait(false) is not { } row)
        {
            return await SeatOnMissingRowAsync(slot, cancellationToken).ConfigureAwait(false);
        }

        var etag = row.ETag;
        var generation = row.Generation + 1;
        row.Owner = slot;
        row.Generation = generation;
        row.ChangedAt = _clock.GetUtcNow();
        try
        {
            await _leaseTable.UpdateEntityAsync(row, etag, TableUpdateMode.Replace, cancellationToken).ConfigureAwait(false);
            Cache(slot, generation);
            return true;
        }
        catch (RequestFailedException error) when (error.Status == 412)
        {
            // Somebody wrote the row between the read and the write: this silo is fenced, not the winner.
            _logger.LogWarning("Slot '{Slot}' lost the active-slot compare-and-swap to '{Candidate}'.", Slot, slot);
            await RefreshAsync(cancellationToken).ConfigureAwait(false);
            return false;
        }
    }

    public async Task ReleaseAsync(CancellationToken cancellationToken = default)
    {
        if (await ReadAsync(cancellationToken).ConfigureAwait(false) is not { } row
            || !string.Equals(row.Owner, Slot, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var etag = row.ETag;
        var generation = row.Generation + 1;
        row.Owner = string.Empty;
        row.Generation = generation;
        row.ChangedAt = _clock.GetUtcNow();
        try
        {
            await _leaseTable.UpdateEntityAsync(row, etag, TableUpdateMode.Replace, cancellationToken).ConfigureAwait(false);
            Cache(string.Empty, generation);
        }
        catch (RequestFailedException error) when (error.Status == 412)
        {
            _logger.LogWarning("Slot '{Slot}' lost the active-slot compare-and-swap while releasing.", Slot);
            await RefreshAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public Task RefreshAsync(CancellationToken cancellationToken = default)
        => ReadAndCacheAsync(cancellationToken);

    // The row does not exist yet: whoever's insert lands first seats that owner. A concurrent insert (409)
    // means someone else won; re-read to see whether it happened to be the owner asked for.
    private async Task<bool> SeatOnMissingRowAsync(string owner, CancellationToken cancellationToken)
    {
        try
        {
            await _leaseTable.AddEntityAsync(
                new ActiveSlotLeaseEntity { Owner = owner, Generation = 1, ChangedAt = _clock.GetUtcNow() },
                cancellationToken).ConfigureAwait(false);
            Cache(owner, 1);
            return true;
        }
        catch (RequestFailedException error) when (error.Status == 409)
        {
            var existing = await ReadAndCacheAsync(cancellationToken).ConfigureAwait(false);
            return existing is not null && string.Equals(existing.Owner, owner, StringComparison.OrdinalIgnoreCase);
        }
    }

    private async Task<ActiveSlotLeaseEntity?> ReadAndCacheAsync(CancellationToken cancellationToken)
    {
        var row = await ReadAsync(cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            ResetCache();
        }
        else
        {
            Cache(row.Owner, row.Generation);
        }

        return row;
    }

    private async Task<ActiveSlotLeaseEntity?> ReadAsync(CancellationToken cancellationToken)
    {
        try
        {
            var response = await _leaseTable.GetEntityAsync<ActiveSlotLeaseEntity>(
                ActiveSlotNames.PartitionKey, ActiveSlotNames.RowKey, cancellationToken: cancellationToken).ConfigureAwait(false);
            return response.Value;
        }
        catch (RequestFailedException error) when (error.Status == 404)
        {
            // No table or no row yet: nobody holds the lease.
            return null;
        }
    }

    private Task EnsureTableAsync(CancellationToken cancellationToken)
    {
        var existing = Volatile.Read(ref _tableReadyTask);
        if (existing is not null)
        {
            return existing;
        }

        // First caller's task is published for everyone to share; CreateTableIfNotExistsAsync tolerates
        // the rare double-fire from a race here, so no stricter guard is needed.
        var created = _tables.CreateTableIfNotExistsAsync(ActiveSlotNames.Table, cancellationToken);
        return Interlocked.CompareExchange(ref _tableReadyTask, created, null) ?? created;
    }

    // Monotonic in Generation: a refresh that read the row before a hand-over can land after it completes,
    // and must not resurrect the verdict it read. Internal so the fact that proves this can drive it
    // directly instead of racing a real hand-over.
    internal void Cache(string? owner, long generation)
    {
        var holds = string.Equals(owner, Slot, StringComparison.OrdinalIgnoreCase);
        Cached current;
        Cached updated;
        do
        {
            current = Volatile.Read(ref _cached);
            if (generation < current.Generation)
            {
                return;
            }

            updated = new Cached(holds, generation);
        }
        while (Interlocked.CompareExchange(ref _cached, updated, current) != current);
    }

    // The row is absent (never bootstrapped, or the table was reset): nobody holds the lease. This bypasses
    // the monotonic check in Cache because "absent" is not a generation the CAS path could ever produce.
    private void ResetCache()
        => Volatile.Write(ref _cached, new Cached(false, 0));

    private sealed record Cached(bool Holds, long Generation);
}
