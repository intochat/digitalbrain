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
    private readonly TableClient _table;
    private readonly TimeProvider _clock;
    private readonly ILogger<AzureTableActiveSlotLease> _logger;
    private Cached _cached = new(false, 0);
    private bool _tableReady;

    public AzureTableActiveSlotLease(string slot, TableServiceClient tables, TimeProvider clock, ILogger<AzureTableActiveSlotLease> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slot);
        ArgumentNullException.ThrowIfNull(tables);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        Slot = slot;
        _tables = tables;
        _table = tables.GetTableClient(ActiveSlotNames.Table);
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

        try
        {
            await _table.AddEntityAsync(
                new ActiveSlotLeaseEntity { Owner = Slot, Generation = 1, Fenced = _clock.GetUtcNow() },
                cancellationToken).ConfigureAwait(false);
            Cache(Slot, 1);
        }
        catch (RequestFailedException error) when (error.Status == 409)
        {
            // Another slot inserted the row first; its owner decides.
            await RefreshAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<bool> TryAcquireAsync(string slot, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slot);
        await EnsureTableAsync(cancellationToken).ConfigureAwait(false);
        if (await ReadAsync(cancellationToken).ConfigureAwait(false) is not { } row)
        {
            await BootstrapAsync(cancellationToken).ConfigureAwait(false);
            return string.Equals(await OwnerAsync(cancellationToken).ConfigureAwait(false), slot, StringComparison.OrdinalIgnoreCase);
        }

        var etag = row.ETag;
        var generation = row.Generation + 1;
        row.Owner = slot;
        row.Generation = generation;
        row.Fenced = _clock.GetUtcNow();
        try
        {
            await _table.UpdateEntityAsync(row, etag, TableUpdateMode.Replace, cancellationToken).ConfigureAwait(false);
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
        row.Fenced = _clock.GetUtcNow();
        try
        {
            await _table.UpdateEntityAsync(row, etag, TableUpdateMode.Replace, cancellationToken).ConfigureAwait(false);
            Cache(string.Empty, generation);
        }
        catch (RequestFailedException error) when (error.Status == 412)
        {
            await RefreshAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var row = await ReadAsync(cancellationToken).ConfigureAwait(false);
        Cache(row?.Owner, row?.Generation ?? 0);
    }

    private async Task<ActiveSlotLeaseEntity?> ReadAsync(CancellationToken cancellationToken)
    {
        try
        {
            var response = await _table.GetEntityAsync<ActiveSlotLeaseEntity>(
                ActiveSlotNames.PartitionKey, ActiveSlotNames.RowKey, cancellationToken: cancellationToken).ConfigureAwait(false);
            return response.Value;
        }
        catch (RequestFailedException error) when (error.Status == 404)
        {
            // No table or no row yet: nobody holds the lease.
            return null;
        }
    }

    private async Task<string?> OwnerAsync(CancellationToken cancellationToken)
        => (await ReadAsync(cancellationToken).ConfigureAwait(false))?.Owner;

    private async Task EnsureTableAsync(CancellationToken cancellationToken)
    {
        if (_tableReady)
        {
            return;
        }

        await _tables.CreateTableIfNotExistsAsync(ActiveSlotNames.Table, cancellationToken).ConfigureAwait(false);
        _tableReady = true;
    }

    private void Cache(string? owner, long generation)
        => Volatile.Write(ref _cached, new Cached(string.Equals(owner, Slot, StringComparison.OrdinalIgnoreCase), generation));

    private sealed record Cached(bool Holds, long Generation);
}
