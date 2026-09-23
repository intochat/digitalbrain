using DigitalBrain.Contracts;

namespace DigitalBrain.Marketplace;

// One earnings account per creator, keyed by creator id. The ledger is fiat-only and every
// mutation is idempotent on a natural key, so a retried charge or refund never doubles.
[Alias("marketplace.earnings")]
[Orleans.Metadata.DefaultGrainType("marketplace-earnings")]
public interface IEarningsAccount : INeuron
{
    Task<EarningsEntry> RecordSaleAsync(AppCharge charge, CancellationToken cancellationToken = default);

    Task<EarningsEntry> RecordRefundAsync(RefundRequest refund, CancellationToken cancellationToken = default);

    Task<EarningsBalance> ReadBalanceAsync(DateTimeOffset asOf, CancellationToken cancellationToken = default);

    Task<EarningsEntry[]> ReadLedgerAsync(CancellationToken cancellationToken = default);

    Task<CreatorOnboarding> OnboardAsync(OnboardingRequest request, CancellationToken cancellationToken = default);

    Task<CreatorOnboarding?> ReadOnboardingAsync(CancellationToken cancellationToken = default);

    Task<PayoutResult> RequestPayoutAsync(DateTimeOffset asOf, CancellationToken cancellationToken = default);

    Task<Payout[]> ReadPayoutsAsync(CancellationToken cancellationToken = default);
}
