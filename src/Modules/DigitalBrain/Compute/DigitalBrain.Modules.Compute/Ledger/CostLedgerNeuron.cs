using DigitalBrain.Core;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Compute.Ledger;

// Platform costs are unbilled and never face a customer, so they live on their own writer
// rather than mixing with wallet charges. Rows are append-only and keyed by idempotency key.
[GrainType("compute-cost-ledger")]
internal sealed class CostLedgerNeuron : Neuron, ICostLedger
{
    private ILedgerStore? store;

    private ILedgerStore Store => store ??= ServiceProvider.GetRequiredService<ILedgerStore>();

    public async Task<LedgerAppend> RecordAsync(LedgerEntry cost, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cost);
        ArgumentException.ThrowIfNullOrWhiteSpace(cost.IdempotencyKey);
        if (cost.Kind != LedgerKind.PlatformCost) { throw new ArgumentException("Entry must be a platform cost.", nameof(cost)); }
        var scoped = cost with { AccountId = this.GetPrimaryKeyString() };
        return await Store.AppendAsync(scoped, cancellationToken).ConfigureAwait(true);
    }

    public async Task<LedgerEntry[]> ReadAsync(CancellationToken cancellationToken = default)
        => [.. await Store.ReadAsync(this.GetPrimaryKeyString(), cancellationToken).ConfigureAwait(true)];
}
