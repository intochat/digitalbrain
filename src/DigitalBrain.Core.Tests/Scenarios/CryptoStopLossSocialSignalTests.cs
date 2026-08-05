using DigitalBrain.Mocks;
using DigitalBrain.Testing;

namespace DigitalBrain.Core.Tests.Scenarios;

public sealed class CryptoStopLossSocialSignalTests(BrainTestClusters clusters) : DigitalBrainTest(clusters)
{
    protected override void Compose(DigitalBrainTestBuilder brain)
        => brain
            .AddModule<RiskPolicy>()
            .AddModule<PortfolioBroker>()
            .AddModule<StopLossDeskLedger>();

    [Fact(DisplayName =
        "Crypto stop-loss: XPostObserved panic → StopLossArmed → PriceTick breach → StopLossTriggered → OrderFilled")]
    public async Task SocialPanicAndPriceCrossArmThenTriggerPortfolioFill()
    {
        var ct = Cancellation;
        var context = "crypto-desk";
        var session = Brain.Session(context);
        var policyId = new NeuronId("riskpolicy", context);
        var brokerId = new NeuronId("portfoliobroker", context);
        var ledgerId = new NeuronId("stoplossdeskledger", context);
        var postId = "x-panic-7";
        var postAt = new DateTimeOffset(2026, 8, 5, 16, 0, 0, TimeSpan.Zero);
        var panicText = "BTC dump incoming — panic selling across the board.";

        await session.EmitAsync(new XPostObserved(postId, "whalewatch", panicText, postAt), ct);

        var afterArm = await WaitForJournalAsync(
            policyId,
            reading => reading.AllHeard<XPostObserved>().Count == 1
                && reading.AllSaid<StopLossArmed>().Count == 1,
            "RiskPolicy heard XPostObserved and said StopLossArmed",
            ct);

        var sessionReading = await ReadAsync(session.Id, ct);
        var postSaid = sessionReading.SaidSingle<XPostObserved>();
        Assert.Equal("declared", postSaid.DeliveryTo(policyId).Via);

        var postHeard = afterArm.HeardSingle<XPostObserved>();
        Assert.Equal(session.Id, postHeard.Metadata.Source);
        Assert.Equal(postSaid.Position, postHeard.Metadata.Sequence);

        var armedSaid = afterArm.SaidSingle<StopLossArmed>();
        Assert.Equal(new SynapseRef(session.Id, postSaid.Position), armedSaid.Cause);
        Assert.Equal("declared", armedSaid.DeliveryTo(ledgerId).Via);
        var armed = Assert.IsType<StopLossArmed>(armedSaid.Body);
        Assert.Equal(RiskPolicy.TrackedAsset, armed.Asset);
        Assert.Equal(RiskPolicy.DefaultStop, armed.StopPrice);
        Assert.Equal(RiskPolicy.DefaultFraction, armed.Fraction);
        Assert.Equal(postId, armed.PostId);

        // Price still above stop — must not trigger.
        await session.EmitAsync(new PriceTick(RiskPolicy.TrackedAsset, 95_000.0), ct);
        var afterSafeTick = await WaitForJournalAsync(
            policyId,
            reading => reading.AllHeard<PriceTick>().Count == 1,
            "RiskPolicy heard above-stop PriceTick",
            ct);
        Assert.Empty(afterSafeTick.AllSaid<StopLossTriggered>());

        await session.EmitAsync(new PriceTick(RiskPolicy.TrackedAsset, 89_500.0), ct);

        var afterTrigger = await WaitForJournalAsync(
            policyId,
            reading => reading.AllSaid<StopLossTriggered>().Count == 1
                && reading.AllHeard<PriceTick>().Count == 2,
            "RiskPolicy said StopLossTriggered after breach",
            ct);

        var brokerReading = await WaitForJournalAsync(
            brokerId,
            reading => reading.AllHeard<StopLossTriggered>().Count == 1
                && reading.AllSaid<OrderFilled>().Count == 1,
            "PortfolioBroker heard StopLossTriggered and said OrderFilled",
            ct);

        sessionReading = await ReadAsync(session.Id, ct);
        var (breachTickSaid, _) = sessionReading.AllSaid<PriceTick>()
            .Select(said => (Said: said, Body: Assert.IsType<PriceTick>(said.Body)))
            .Single(pair => pair.Body.Price == 89_500.0);

        var triggeredSaid = afterTrigger.SaidSingle<StopLossTriggered>();
        Assert.Equal(new SynapseRef(session.Id, breachTickSaid.Position), triggeredSaid.Cause);
        Assert.Equal("declared", triggeredSaid.DeliveryTo(brokerId).Via);
        Assert.Equal("declared", triggeredSaid.DeliveryTo(ledgerId).Via);
        var triggered = Assert.IsType<StopLossTriggered>(triggeredSaid.Body);
        Assert.Equal(RiskPolicy.TrackedAsset, triggered.Asset);
        Assert.Equal(89_500.0, triggered.Price);
        Assert.Equal(RiskPolicy.DefaultFraction, triggered.Fraction);
        Assert.Equal(postId, triggered.PostId);
        Assert.Contains(postId, triggered.Reason, StringComparison.Ordinal);

        var triggerHeard = brokerReading.HeardSingle<StopLossTriggered>();
        Assert.Equal(policyId, triggerHeard.Metadata.Source);
        Assert.Equal(triggeredSaid.Position, triggerHeard.Metadata.Sequence);

        var filledSaid = brokerReading.SaidSingle<OrderFilled>();
        Assert.Equal(new SynapseRef(policyId, triggeredSaid.Position), filledSaid.Cause);
        Assert.Equal("declared", filledSaid.DeliveryTo(ledgerId).Via);
        var filled = Assert.IsType<OrderFilled>(filledSaid.Body);
        Assert.Equal(RiskPolicy.TrackedAsset, filled.Asset);
        Assert.Equal(RiskPolicy.DefaultFraction, filled.Fraction);
        Assert.Equal(89_500.0, filled.FillPrice);
        Assert.Equal(postId, filled.PostId);

        // Second breach tick must not double-sell (journal gate on State.Triggered).
        await session.EmitAsync(new PriceTick(RiskPolicy.TrackedAsset, 88_000.0), ct);
        var afterDup = await WaitForJournalAsync(
            policyId,
            reading => reading.AllHeard<PriceTick>().Count == 3,
            "third PriceTick heard",
            ct);
        Assert.Single(afterDup.AllSaid<StopLossTriggered>());
        Assert.Single((await ReadAsync(brokerId, ct)).AllSaid<OrderFilled>());

        var ledgerReading = await WaitForJournalAsync(
            ledgerId,
            reading => reading.AllHeard<StopLossArmed>().Count == 1
                && reading.AllHeard<StopLossTriggered>().Count == 1
                && reading.AllHeard<OrderFilled>().Count == 1,
            "desk ledger heard arm, trigger, and fill",
            ct);
        Assert.Equal(policyId, ledgerReading.HeardSingle<StopLossArmed>().Metadata.Source);
        Assert.Equal(policyId, ledgerReading.HeardSingle<StopLossTriggered>().Metadata.Source);
        Assert.Equal(brokerId, ledgerReading.HeardSingle<OrderFilled>().Metadata.Source);
    }

    [Fact(DisplayName =
        "Crypto stop-loss: social injection without panic join does not arm; price alone never sells")]
    public async Task NonPanicSocialAndPriceAloneNeverTrigger()
    {
        var ct = Cancellation;
        var context = "crypto-no-arm";
        var session = Brain.Session(context);
        var policyId = new NeuronId("riskpolicy", context);

        await session.EmitAsync(
            new XPostObserved(
                "x-benign",
                "analyst",
                "BTC looking steady this week.",
                new DateTimeOffset(2026, 8, 5, 17, 0, 0, TimeSpan.Zero)),
            ct);

        var afterBenign = await WaitForJournalAsync(
            policyId,
            reading => reading.AllHeard<XPostObserved>().Count == 1,
            "benign XPostObserved heard",
            ct);
        Assert.Empty(afterBenign.AllSaid<StopLossArmed>());

        await session.EmitAsync(new PriceTick(RiskPolicy.TrackedAsset, 80_000.0), ct);
        var afterTick = await WaitForJournalAsync(
            policyId,
            reading => reading.AllHeard<PriceTick>().Count == 1,
            "PriceTick heard without arm",
            ct);
        Assert.Empty(afterTick.AllSaid<StopLossTriggered>());
        Assert.Empty((await ReadAsync(new NeuronId("portfoliobroker", context), ct)).Journal);
    }
}
