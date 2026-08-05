using DigitalBrain.Testing;

namespace DigitalBrain.Core.Tests.Scenarios;

public sealed class CustomerChurnAlertCascadeTests(BrainTestClusters clusters) : DigitalBrainTest(clusters)
{
    protected override void Compose(DigitalBrainTestBuilder brain)
        => brain
            .AddModule<ChurnEngine>()
            .AddModule<ChurnDeskLedger>();

    [Fact(DisplayName =
        "Customer churn cascade: ambient ticket+usage+champion scores → ChurnCaseOpened + SavePlayProposed without user prompt")]
    public async Task MultiSignalThresholdOpensCaseAndSavePlay()
    {
        var ct = Cancellation;
        var context = "churn-desk";
        var session = Brain.Session(context);
        var engineId = new NeuronId("churnengine", context);
        var ledgerId = new NeuronId("churndeskledger", context);
        var accountId = "acme";

        // ticket 40 + usage 35 = 75 ≥ threshold → case opens; champion would be extra if earlier.
        await session.EmitAsync(new SupportTicketOpened(accountId, "t-9", Severity: 3), ct);
        await session.EmitAsync(new UsageDropped(accountId, DropPct: 0.4), ct);

        var engineReading = await WaitForJournalAsync(
            engineId,
            reading => reading.AllSaid<ChurnCaseOpened>().Count == 1
                && reading.AllSaid<SavePlayProposed>().Count == 1
                && reading.AllSaid<ChurnRiskScoreUpdated>().Count >= 2,
            "threshold crossed → case + save play",
            ct);

        var ledgerReading = await WaitForJournalAsync(
            ledgerId,
            reading => reading.AllHeard<ChurnCaseOpened>().Count == 1
                && reading.AllHeard<SavePlayProposed>().Count == 1
                && reading.AllHeard<ChurnAlertSurfaced>().Count == 1,
            "desk ledger heard case + play + alert",
            ct);

        var sessionReading = await ReadAsync(session.Id, ct);
        var ticketSaid = sessionReading.SaidSingle<SupportTicketOpened>();
        var usageSaid = sessionReading.SaidSingle<UsageDropped>();
        Assert.Equal("declared", ticketSaid.DeliveryTo(engineId).Via);
        Assert.Equal("declared", usageSaid.DeliveryTo(engineId).Via);

        var scoreUpdates = engineReading.AllSaid<ChurnRiskScoreUpdated>();
        Assert.True(scoreUpdates.Count >= 2);
        Assert.All(scoreUpdates, said => Assert.Equal("declared", said.DeliveryTo(ledgerId).Via));

        var caseSaid = engineReading.SaidSingle<ChurnCaseOpened>();
        Assert.Equal("declared", caseSaid.DeliveryTo(ledgerId).Via);
        var opened = Assert.IsType<ChurnCaseOpened>(caseSaid.Body);
        Assert.Equal($"churn-{accountId}", opened.CaseId);
        Assert.Equal(accountId, opened.AccountId);
        Assert.True(opened.Score >= ChurnEngine.Threshold);

        var playSaid = engineReading.SaidSingle<SavePlayProposed>();
        Assert.Equal(opened.CaseId, Assert.IsType<SavePlayProposed>(playSaid.Body).CaseId);
        Assert.Contains("execEmail", Assert.IsType<SavePlayProposed>(playSaid.Body).Options);

        Assert.Equal(engineId, ledgerReading.HeardSingle<ChurnCaseOpened>().Metadata.Source);
        Assert.Equal(caseSaid.Position, ledgerReading.HeardSingle<ChurnCaseOpened>().Metadata.Sequence);

        // Double-open gate: champion after case does not open a second case.
        await session.EmitAsync(new ChampionSignalObserved(accountId, "left-company"), ct);
        var afterChampion = await WaitForJournalAsync(
            engineId,
            reading => reading.AllHeard<ChampionSignalObserved>().Count == 1
                && reading.AllSaid<ChurnRiskScoreUpdated>().Count >= 3,
            "champion scored after open case",
            ct);
        Assert.Single(afterChampion.AllSaid<ChurnCaseOpened>());
        Assert.Single(afterChampion.AllSaid<SavePlayProposed>());
    }
}
