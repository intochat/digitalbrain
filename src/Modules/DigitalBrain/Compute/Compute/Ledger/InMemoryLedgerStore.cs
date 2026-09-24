using System.Collections.Concurrent;

namespace DigitalBrain.Compute.Ledger;

// Append-only ledger with the idempotency key as its uniqueness constraint. Every
// writer shares the store, so two callers charging the same key insert exactly one row.
internal sealed class InMemoryLedgerStore : ILedgerStore
{
    private readonly ConcurrentDictionary<(string AccountId, string IdempotencyKey), LedgerEntry> entries = new();

    public ValueTask<LedgerAppend> AppendAsync(LedgerEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        cancellationToken.ThrowIfCancellationRequested();
        var appended = entries.TryAdd((entry.AccountId, entry.IdempotencyKey), entry);
        return ValueTask.FromResult(appended ? LedgerAppend.Inserted : LedgerAppend.Duplicate);
    }

    public ValueTask<IReadOnlyList<LedgerEntry>> ReadAsync(string accountId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<LedgerEntry> ledger = [.. entries.Values
            .Where(entry => string.Equals(entry.AccountId, accountId, StringComparison.Ordinal))
            .OrderBy(entry => entry.OccurredAt)];
        return ValueTask.FromResult(ledger);
    }
}
