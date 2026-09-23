using DigitalBrain.Core;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Compute.Ledger;

// One activation per account is the single writer for that account's ledger. Every charge
// carries an idempotency key, so a retried or duplicated request is recorded once.
[GrainType("compute-wallet")]
internal sealed class WalletNeuron : Neuron, IWallet
{
    private ILedgerStore? store;

    private ILedgerStore Store => store ??= ServiceProvider.GetRequiredService<ILedgerStore>();

    public async Task<LedgerAppend> ChargeAsync(LedgerEntry charge, CancellationToken cancellationToken = default)
        => await AppendAsync(Require(charge, LedgerKind.WalletCharge), cancellationToken).ConfigureAwait(true);

    public async Task<LedgerAppend> RecordCostAsync(LedgerEntry cost, CancellationToken cancellationToken = default)
        => await AppendAsync(Require(cost, LedgerKind.PlatformCost), cancellationToken).ConfigureAwait(true);

    public async Task<WalletBalance> ReadBalanceAsync(CancellationToken cancellationToken = default)
    {
        var accountId = this.GetPrimaryKeyString();
        var entries = await Store.ReadAsync(accountId, cancellationToken).ConfigureAwait(true);
        var charged = entries.Where(entry => entry.Kind == LedgerKind.WalletCharge).Sum(entry => entry.Amount);
        var cost = entries.Where(entry => entry.Kind == LedgerKind.PlatformCost).Sum(entry => entry.Amount);
        return new WalletBalance(accountId, charged, cost, entries.Count);
    }

    public async Task<LedgerEntry[]> ReadEntriesAsync(CancellationToken cancellationToken = default)
        => [.. await Store.ReadAsync(this.GetPrimaryKeyString(), cancellationToken).ConfigureAwait(true)];

    private async Task<LedgerAppend> AppendAsync(LedgerEntry entry, CancellationToken cancellationToken)
    {
        var scoped = entry with { AccountId = this.GetPrimaryKeyString() };
        return await Store.AppendAsync(scoped, cancellationToken).ConfigureAwait(true);
    }

    private static LedgerEntry Require(LedgerEntry entry, LedgerKind kind)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.IdempotencyKey);
        if (entry.Kind != kind) { throw new ArgumentException($"Entry must be {kind}.", nameof(entry)); }
        return entry;
    }
}
