using DigitalBrain.Testing;

namespace DigitalBrain.Core.Tests.Scenarios;

public sealed class CryptoWalletTaxJournalTests(BrainTestClusters clusters) : DigitalBrainTest(clusters)
{
    protected override void Compose(DigitalBrainTestBuilder brain)
        => brain
            .AddModule<TaxLots>()
            .AddModule<TaxPriceOracle>()
            .AddModule<TaxLotLedger>();

    [Fact(DisplayName =
        "Crypto wallet tax journal: OnChainTransferObserved in → HistoricalPrice ask → TaxLotOpened; out → FIFO TaxLotDisposed")]
    public async Task InboundOpensLotOutboundFifoDisposes()
    {
        var ct = Cancellation;
        var context = "wallet-tax";
        var session = Brain.Session(context);
        var taxId = new NeuronId("taxlots", context);
        var oracleId = new NeuronId("taxpriceoracle", context);
        var ledgerId = new NeuronId("taxlotledger", context);
        var buyHash = "0xbuy1";
        var sellHash = "0xsell1";
        var blockTime = new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

        await session.EmitAsync(
            new OnChainTransferObserved(buyHash, "BTC", 1.0m, "in", "exchange", blockTime),
            ct);

        var afterBuy = await WaitForJournalAsync(
            taxId,
            reading => reading.AllSaid<TaxLotOpened>().Count == 1
                && reading.AllHeard<HistoricalPriceAnswered>().Count == 1,
            "TaxLotOpened after price answer on inbound",
            ct);

        var buySession = await ReadAsync(session.Id, ct);
        var transferSaid = buySession.SaidSingle<OnChainTransferObserved>();
        Assert.Equal("declared", transferSaid.DeliveryTo(taxId).Via);

        var priceAsk = afterBuy.SaidSingle<HistoricalPriceAsked>();
        Assert.Equal("ask", priceAsk.DeliveryTo(oracleId).Via);
        Assert.Equal(new SynapseRef(session.Id, transferSaid.Position), priceAsk.Cause);

        var priceAnswer = afterBuy.HeardSingle<HistoricalPriceAnswered>();
        Assert.Equal(oracleId, priceAnswer.Metadata.Source);
        Assert.Equal(new SynapseRef(taxId, priceAsk.Position), priceAnswer.Answers);
        Assert.Equal(100_000m, Assert.IsType<HistoricalPriceAnswered>(priceAnswer.Body).PriceUsd);

        var openedSaid = afterBuy.SaidSingle<TaxLotOpened>();
        Assert.Equal(new SynapseRef(oracleId, priceAnswer.Metadata.Sequence), openedSaid.Cause);
        Assert.Equal("declared", openedSaid.DeliveryTo(ledgerId).Via);
        var opened = Assert.IsType<TaxLotOpened>(openedSaid.Body);
        Assert.Equal($"lot-{buyHash}", opened.LotId);
        Assert.Equal(buyHash, opened.TxHash);
        Assert.Equal(1.0m, opened.Qty);
        Assert.Equal(100_000m, opened.BasisUsd);

        await session.EmitAsync(
            new OnChainTransferObserved(sellHash, "BTC", 1.0m, "out", "cold-wallet", blockTime.AddHours(1)),
            ct);

        var afterSell = await WaitForJournalAsync(
            taxId,
            reading => reading.AllSaid<TaxLotDisposed>().Count == 1
                && reading.AllHeard<HistoricalPriceAnswered>().Count == 2,
            "TaxLotDisposed FIFO after outbound",
            ct);

        var disposedSaid = afterSell.SaidSingle<TaxLotDisposed>();
        Assert.Equal("declared", disposedSaid.DeliveryTo(ledgerId).Via);
        var disposed = Assert.IsType<TaxLotDisposed>(disposedSaid.Body);
        Assert.Equal(sellHash, disposed.TxHash);
        Assert.Equal("FIFO", disposed.Method);
        Assert.Equal(["lot-0xbuy1"], disposed.ConsumedLots);
        Assert.Equal(100_000m, disposed.ProceedsUsd);
        Assert.Equal(0m, disposed.GainUsd);

        var ledgerReading = await WaitForJournalAsync(
            ledgerId,
            reading => reading.AllHeard<TaxLotOpened>().Count == 1
                && reading.AllHeard<TaxLotDisposed>().Count == 1,
            "ledger heard open + dispose",
            ct);
        Assert.Equal(taxId, ledgerReading.HeardSingle<TaxLotOpened>().Metadata.Source);
        Assert.Equal(taxId, ledgerReading.HeardSingle<TaxLotDisposed>().Metadata.Source);
    }
}
