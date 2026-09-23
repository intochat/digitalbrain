using DigitalBrain.Contracts;
using DigitalBrain.Core;
using DigitalBrain.Marketplace.Signals;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.Runtime;

namespace DigitalBrain.Marketplace;

[GenerateSerializer, Alias("marketplace.earnings-state")]
internal sealed record EarningsState
{
    [Id(0)] public List<EarningsEntry> Entries { get; init; } = [];
    [Id(1)] public List<Payout> Payouts { get; init; } = [];
    [Id(2)] public CreatorOnboarding? Onboarding { get; set; }
}

// One earnings account per creator is the single writer for the fiat ledger and payouts. Every
// mutation is keyed by a natural id (charge id, refund, payout) so a retried charge or refund adds
// no second entry.
[GrainType("marketplace-earnings")]
internal sealed class EarningsAccountNeuron(
    [PersistentState("earnings", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<EarningsState> store)
    : Neuron, IEarningsAccount
{
    private string CreatorId => this.GetPrimaryKeyString();

    private EarningsState State
    {
        get
        {
            store.State ??= new EarningsState();
            return store.State;
        }
    }

    private MarketplaceEarningsOptions Settings =>
        ServiceProvider.GetService<IOptions<MarketplaceEarningsOptions>>()?.Value ?? new MarketplaceEarningsOptions();

    private IPayoutProvider Payouts => ServiceProvider.GetService<IPayoutProvider>() ?? FakePayoutProvider.Instance;

    private ICreatorOnboardingProvider OnboardingProvider =>
        ServiceProvider.GetService<ICreatorOnboardingProvider>()
        ?? new FakeCreatorOnboardingProvider(ServiceProvider.GetService<ISanctionsScreener>() ?? FakeSanctionsScreener.Instance);

    public async Task<EarningsEntry> RecordSaleAsync(AppCharge charge, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(charge);
        var entryId = $"sale:{charge.ChargeId}";
        if (Find(entryId) is { } existing) { return existing; }

        var occurredAt = charge.OccurredAt == default ? DateTimeOffset.UtcNow : charge.OccurredAt;
        var (fee, net) = MerchantOfRecord.Split(charge.GrossUsd, charge.TaxUsd, Settings.TakeRate);
        var entry = new EarningsEntry
        {
            EntryId = entryId,
            Kind = EarningsEntryKind.Sale,
            CreatorId = CreatorId,
            AppId = charge.AppId,
            ChargeId = charge.ChargeId,
            GrossUsd = charge.GrossUsd,
            TaxUsd = charge.TaxUsd,
            FeeUsd = fee,
            NetUsd = net,
            OccurredAt = occurredAt,
            AvailableAt = occurredAt + Settings.HoldingPeriod,
        };
        await AppendAsync(entry);
        return entry;
    }

    public async Task<EarningsEntry> RecordRefundAsync(RefundRequest refund, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(refund);
        var entryId = $"refund:{refund.ChargeId}";
        if (Find(entryId) is { } existing) { return existing; }

        var sale = State.Entries.Find(item =>
            item.Kind == EarningsEntryKind.Sale && string.Equals(item.ChargeId, refund.ChargeId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"No sale charge '{refund.ChargeId}' to refund.");
        var occurredAt = refund.OccurredAt == default ? DateTimeOffset.UtcNow : refund.OccurredAt;
        var entry = new EarningsEntry
        {
            EntryId = entryId,
            Kind = EarningsEntryKind.Refund,
            CreatorId = CreatorId,
            AppId = sale.AppId,
            ChargeId = sale.ChargeId,
            GrossUsd = -sale.GrossUsd,
            TaxUsd = -sale.TaxUsd,
            FeeUsd = -sale.FeeUsd,
            NetUsd = -sale.NetUsd,
            OccurredAt = occurredAt,
            AvailableAt = occurredAt,
            Description = refund.Reason,
        };
        await AppendAsync(entry);
        return entry;
    }

    public Task<EarningsBalance> ReadBalanceAsync(DateTimeOffset asOf, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Balance(asOf));
    }

    public Task<EarningsEntry[]> ReadLedgerAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(State.Entries.ToArray());
    }

    public async Task<CreatorOnboarding> OnboardAsync(OnboardingRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        var onboarding = await OnboardingProvider.OnboardAsync(request, cancellationToken).ConfigureAwait(true);
        State.Onboarding = onboarding;
        await store.WriteStateAsync();
        return onboarding;
    }

    public Task<CreatorOnboarding?> ReadOnboardingAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(State.Onboarding);
    }

    public async Task<PayoutResult> RequestPayoutAsync(DateTimeOffset asOf, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var onboarding = State.Onboarding;
        if (onboarding is null || !onboarding.CanReceivePayouts)
        {
            return new PayoutResult
            {
                Outcome = PayoutOutcome.Blocked,
                Reason = onboarding?.Reason ?? "The creator is not onboarded for payouts.",
            };
        }

        var balance = Balance(asOf);
        if (balance.AvailableUsd < Settings.PayoutThresholdUsd)
        {
            return new PayoutResult
            {
                Outcome = PayoutOutcome.BelowThreshold,
                Reason = $"Available ${balance.AvailableUsd:0.00} is below the ${Settings.PayoutThresholdUsd:0.00} threshold.",
            };
        }

        var payoutId = $"payout_{CreatorId}_{State.Payouts.Count + 1}";
        var account = await Payouts.CreateConnectedAccountAsync(new ConnectAccountRequest
        {
            CreatorId = CreatorId,
            Country = onboarding.CountryCode ?? "unknown",
            LegalName = onboarding.LegalName ?? CreatorId,
        }, cancellationToken).ConfigureAwait(true);
        var transfer = await Payouts.TransferAsync(new PayoutTransferRequest
        {
            AccountId = account.AccountId,
            PayoutId = payoutId,
            AmountUsd = balance.AvailableUsd,
            Currency = "usd",
        }, cancellationToken).ConfigureAwait(true);

        var payout = new Payout
        {
            PayoutId = payoutId,
            CreatorId = CreatorId,
            AmountUsd = balance.AvailableUsd,
            Paid = transfer.Succeeded,
            ProviderReference = transfer.ProviderReference,
            FailureReason = transfer.FailureReason,
            RequestedAt = asOf,
        };
        var next = State;
        next.Payouts.Add(payout);
        next.Entries.Add(new EarningsEntry
        {
            EntryId = $"payout:{payoutId}",
            Kind = EarningsEntryKind.Payout,
            CreatorId = CreatorId,
            GrossUsd = -payout.AmountUsd,
            TaxUsd = 0m,
            FeeUsd = 0m,
            NetUsd = -payout.AmountUsd,
            OccurredAt = asOf,
            AvailableAt = asOf,
            PayoutId = payoutId,
            Description = transfer.Succeeded ? "Payout settled to the connected account." : "Payout attempt failed.",
        });
        await store.WriteStateAsync();
        await PublishAsync(new PayoutSettled(payout.PayoutId, CreatorId, payout.AmountUsd, payout.Paid, asOf));

        return new PayoutResult
        {
            Outcome = transfer.Succeeded ? PayoutOutcome.Paid : PayoutOutcome.Failed,
            Payout = payout,
            Reason = transfer.FailureReason,
        };
    }

    public Task<Payout[]> ReadPayoutsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(State.Payouts.ToArray());
    }

    private EarningsBalance Balance(DateTimeOffset asOf)
    {
        var entries = State.Entries.Where(entry => entry.Kind != EarningsEntryKind.Payout).ToArray();
        var paidOut = State.Payouts.Where(payout => payout.Paid).Sum(payout => payout.AmountUsd);
        var earned = entries.Where(entry => entry.AvailableAt <= asOf).Sum(entry => entry.NetUsd);
        var held = entries.Where(entry => entry.AvailableAt > asOf).Sum(entry => entry.NetUsd);
        return new EarningsBalance
        {
            AvailableUsd = MerchantOfRecord.Round(earned - paidOut),
            HeldUsd = MerchantOfRecord.Round(held),
            PaidOutUsd = MerchantOfRecord.Round(paidOut),
            LifetimeNetUsd = MerchantOfRecord.Round(entries.Sum(entry => entry.NetUsd)),
        };
    }

    private EarningsEntry? Find(string entryId) =>
        State.Entries.Find(entry => string.Equals(entry.EntryId, entryId, StringComparison.Ordinal));

    private async Task AppendAsync(EarningsEntry entry)
    {
        State.Entries.Add(entry);
        await store.WriteStateAsync();
        await PublishAsync(new EarningsRecorded(entry.EntryId, entry.CreatorId, entry.Kind, entry.NetUsd, entry.OccurredAt));
    }
}
