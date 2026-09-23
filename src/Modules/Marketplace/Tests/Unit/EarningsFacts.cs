using DigitalBrain.Marketplace;
using DigitalBrain.Marketplace.Signals;
using DigitalBrain.Testing.Unit;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class EarningsFacts
{
    private const string Creator = "creator-acme";
    private static readonly DateTimeOffset SaleAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task PayoutRunsEndToEndOnTheFakeProviderAndRespectsTheHold()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await StartAsync(ct);
        var account = brain.Get<IEarningsAccount>(Creator);
        await using var events = await brain.Observe<EarningsRecorded>(account, ct);

        var onboarding = await account.OnboardAsync(
            new OnboardingRequest { CreatorId = Creator, LegalName = "Acme GmbH", Country = "DE" }, ct);
        Assert.True(onboarding.CanPublishPaidApps);

        var sale = await account.RecordSaleAsync(
            new AppCharge { ChargeId = "ch-1", AppId = "app.ledger", GrossUsd = 100m, TaxUsd = 19m, OccurredAt = SaleAt }, ct);
        Assert.Equal(EarningsEntryKind.Sale, sale.Kind);
        Assert.Equal(100m, sale.GrossUsd);
        Assert.Equal(19m, sale.TaxUsd);
        Assert.Equal(4.05m, sale.FeeUsd);
        Assert.Equal(76.95m, sale.NetUsd);

        var recorded = await events.NextAsync(ct: ct);
        Assert.Equal(sale.EntryId, recorded.EntryId);

        var held = await account.ReadBalanceAsync(SaleAt + TimeSpan.FromDays(119), ct);
        Assert.Equal(76.95m, held.HeldUsd);
        Assert.Equal(0m, held.AvailableUsd);

        var tooEarly = await account.RequestPayoutAsync(SaleAt + TimeSpan.FromDays(119), ct);
        Assert.Equal(PayoutOutcome.BelowThreshold, tooEarly.Outcome);
        Assert.Empty(await account.ReadPayoutsAsync(ct));

        var afterHold = SaleAt + TimeSpan.FromDays(121);
        var result = await account.RequestPayoutAsync(afterHold, ct);
        Assert.Equal(PayoutOutcome.Paid, result.Outcome);
        Assert.NotNull(result.Payout);
        Assert.True(result.Payout!.Paid);
        Assert.StartsWith("payout_fake_", result.Payout.ProviderReference, StringComparison.Ordinal);
        Assert.Equal(76.95m, result.Payout.AmountUsd);

        var balance = await account.ReadBalanceAsync(afterHold, ct);
        Assert.Equal(0m, balance.AvailableUsd);
        Assert.Equal(76.95m, balance.PaidOutUsd);
        Assert.Equal(76.95m, balance.LifetimeNetUsd);

        var ledger = await account.ReadLedgerAsync(ct);
        Assert.Contains(ledger, entry => entry.Kind == EarningsEntryKind.Payout && entry.NetUsd == -76.95m);
    }

    [Fact]
    public async Task RefundIsANewEntryThatReversesTheSaleAndIsIdempotent()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await StartAsync(ct);
        var account = brain.Get<IEarningsAccount>(Creator);
        await account.OnboardAsync(new OnboardingRequest { CreatorId = Creator, LegalName = "Acme GmbH", Country = "DE" }, ct);

        var sale = await account.RecordSaleAsync(
            new AppCharge { ChargeId = "ch-2", AppId = "app.ledger", GrossUsd = 50m, TaxUsd = 0m, OccurredAt = SaleAt }, ct);
        var refund = await account.RecordRefundAsync(
            new RefundRequest { ChargeId = "ch-2", OccurredAt = SaleAt + TimeSpan.FromDays(5), Reason = "customer refund" }, ct);

        Assert.Equal(EarningsEntryKind.Refund, refund.Kind);
        Assert.Equal(-50m, refund.GrossUsd);
        Assert.Equal(-2.5m, refund.FeeUsd);
        Assert.Equal(-47.5m, refund.NetUsd);

        var ledger = await account.ReadLedgerAsync(ct);
        Assert.Equal(2, ledger.Length);
        var original = Assert.Single(ledger, entry => entry.EntryId == sale.EntryId);
        Assert.Equal(47.5m, original.NetUsd);
        Assert.Null(original.PayoutId);

        await account.RecordRefundAsync(
            new RefundRequest { ChargeId = "ch-2", OccurredAt = SaleAt + TimeSpan.FromDays(6) }, ct);
        Assert.Equal(2, (await account.ReadLedgerAsync(ct)).Length);

        var balance = await account.ReadBalanceAsync(SaleAt + TimeSpan.FromDays(121), ct);
        Assert.Equal(0m, balance.AvailableUsd);
        Assert.Equal(0m, balance.LifetimeNetUsd);
    }

    [Fact]
    public async Task CreatorThatCannotOnboardPublishesFreeAppsOnly()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await StartAsync(ct);
        var account = brain.Get<IEarningsAccount>(Creator);

        var onboarding = await account.OnboardAsync(
            new OnboardingRequest { CreatorId = Creator, LegalName = "Acme Ltd", Country = "ZZ" }, ct);

        Assert.Equal(CountrySupport.Unsupported, onboarding.Country);
        Assert.False(onboarding.CanReceivePayouts);
        Assert.False(onboarding.CanPublishPaidApps);

        var payout = await account.RequestPayoutAsync(SaleAt + TimeSpan.FromDays(365), ct);
        Assert.Equal(PayoutOutcome.Blocked, payout.Outcome);
        Assert.Empty(await account.ReadPayoutsAsync(ct));
    }

    [Fact]
    public async Task SanctionsScreeningAdapterBlocksAPayout()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await StartAsync(ct);
        var account = brain.Get<IEarningsAccount>(Creator);

        var onboarding = await account.OnboardAsync(
            new OnboardingRequest { CreatorId = Creator, LegalName = "Acme Ltd", Country = "IR" }, ct);

        Assert.False(onboarding.SanctionsCleared);
        Assert.False(onboarding.CanPublishPaidApps);
        Assert.Contains("Sanctions", onboarding.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TakeRateDefaultsToTheBetaMerchantOfRecordFeeAndIsConfigurable()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var beta = await StartAsync(ct);
        var betaAccount = beta.Get<IEarningsAccount>(Creator);
        var betaStartup = await betaAccount.RecordSaleAsync(
            new AppCharge { ChargeId = "ch-beta", AppId = "app.ledger", GrossUsd = 100m, TaxUsd = 0m, OccurredAt = SaleAt }, ct);
        Assert.Equal(5m, betaStartup.FeeUsd);
        Assert.Equal(95m, betaStartup.NetUsd);

        await using var configured = await StartAsync(ct, options =>
        {
            options.MerchantOfRecordFeeRate = 0.05m;
            options.PlatformFeeRate = 0.10m;
        });
        var configuredAccount = configured.Get<IEarningsAccount>(Creator);
        var configuredSale = await configuredAccount.RecordSaleAsync(
            new AppCharge { ChargeId = "ch-beta", AppId = "app.ledger", GrossUsd = 100m, TaxUsd = 0m, OccurredAt = SaleAt }, ct);
        Assert.Equal(15m, configuredSale.FeeUsd);
        Assert.Equal(85m, configuredSale.NetUsd);
    }

    [Fact]
    public void EarningsLedgerIsFiatOnlyAndNeverCarriesCompute()
    {
        var ledgerTypes = new[]
        {
            typeof(EarningsEntry), typeof(EarningsBalance), typeof(Payout), typeof(AppCharge), typeof(RefundRequest),
        };
        var offenders = ledgerTypes
            .SelectMany(type => type.GetProperties())
            .Where(property => property.Name.Contains("Compute", StringComparison.OrdinalIgnoreCase))
            .Select(property => $"{property.DeclaringType!.Name}.{property.Name}")
            .ToArray();
        Assert.True(offenders.Length == 0, $"Earnings are fiat-only; Compute must not appear: {string.Join(", ", offenders)}");
    }

    private static Task<UnitBrain> StartAsync(CancellationToken ct, Action<MarketplaceEarningsOptions>? configure = null) =>
        UnitTest.Create()
            .ConfigureSilo(silo => silo.Services.AddMarketplaceEarnings(configure))
            .StartAsync(ct);
}
