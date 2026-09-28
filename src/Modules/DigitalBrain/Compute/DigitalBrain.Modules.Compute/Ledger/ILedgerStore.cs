namespace DigitalBrain.Compute.Ledger;

internal interface ILedgerStore
{
    ValueTask<decimal> ChargedAsync(string accountId, string? intentId, CancellationToken cancellationToken = default);

    ValueTask<LedgerAppend> AppendAsync(LedgerEntry entry, CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<LedgerEntry>> ReadAsync(string accountId, CancellationToken cancellationToken = default);
}
