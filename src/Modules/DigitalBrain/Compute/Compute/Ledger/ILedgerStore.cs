namespace DigitalBrain.Compute.Ledger;

internal interface ILedgerStore
{
    ValueTask<LedgerAppend> AppendAsync(LedgerEntry entry, CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<LedgerEntry>> ReadAsync(string accountId, CancellationToken cancellationToken = default);
}
