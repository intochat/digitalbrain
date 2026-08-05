using DigitalBrain.Testing;

namespace DigitalBrain.Core.Tests.Scenarios;

public sealed class OpportunityCloseGmailSequenceTests(BrainTestClusters clusters)
    : DigitalBrainTest(clusters)
{
    protected override void Compose(DigitalBrainTestBuilder brain)
        => brain
            .AddModule<WinSequenceRunner>()
            .AddModule<WinSequenceLedger>();

    [Fact(DisplayName =
        "Opportunity close sequence: ClosedWon → WinSequenceStarted + thank-you email + internal notify + CS task + calendar + completed")]
    public async Task ClosedWonFansOutCoordinatedSequence()
    {
        var ct = Cancellation;
        var context = "win-northwind";
        var session = Brain.Session(context);
        var runnerId = new NeuronId("winsequencerunner", context);
        var ledgerId = new NeuronId("winsequenceledger", context);
        var oppId = "opp-northwind-1";

        await session.EmitAsync(
            new OpportunityStageChanged(
                oppId,
                FromStage: "Negotiation",
                ToStage: "ClosedWon",
                Amount: 85_000,
                ChampionEmail: "champ@northwind.test"),
            ct);

        var runner = await WaitForJournalAsync(
            runnerId,
            reading => reading.AllSaid<WinSequenceStarted>().Count == 1
                && reading.AllSaid<WinThankYouEmailSent>().Count == 1
                && reading.AllSaid<InternalWinNotified>().Count == 1
                && reading.AllSaid<WinCsTaskCreated>().Count == 1
                && reading.AllSaid<WinKickoffCalendarCreated>().Count == 1
                && reading.AllSaid<WinSequenceCompleted>().Count == 1,
            "full win sequence journaled",
            ct);

        var ledger = await WaitForJournalAsync(
            ledgerId,
            reading => reading.AllHeard<WinSequenceStarted>().Count == 1
                && reading.AllHeard<WinThankYouEmailSent>().Count == 1
                && reading.AllHeard<InternalWinNotified>().Count == 1
                && reading.AllHeard<WinCsTaskCreated>().Count == 1
                && reading.AllHeard<WinKickoffCalendarCreated>().Count == 1
                && reading.AllHeard<WinSequenceCompleted>().Count == 1,
            "ledger heard full sequence",
            ct);

        var sessionReading = await ReadAsync(session.Id, ct);
        var stageSaid = sessionReading.SaidSingle<OpportunityStageChanged>();
        Assert.Equal("declared", stageSaid.DeliveryTo(runnerId).Via);

        var started = runner.SaidSingle<WinSequenceStarted>();
        Assert.Equal(new SynapseRef(session.Id, stageSaid.Position), started.Cause);
        Assert.Equal("declared", started.DeliveryTo(ledgerId).Via);
        Assert.Equal(oppId, Assert.IsType<WinSequenceStarted>(started.Body).OppId);

        var email = Assert.IsType<WinThankYouEmailSent>(runner.SaidSingle<WinThankYouEmailSent>().Body);
        Assert.Equal("champ@northwind.test", email.To);
        Assert.Contains(oppId, email.Subject, StringComparison.Ordinal);

        Assert.Equal(85_000, Assert.IsType<InternalWinNotified>(
            runner.SaidSingle<InternalWinNotified>().Body).Amount);
        Assert.Contains("onboarding", Assert.IsType<WinCsTaskCreated>(
            runner.SaidSingle<WinCsTaskCreated>().Body).Title, StringComparison.Ordinal);
        Assert.Equal(runnerId, ledger.HeardSingle<WinSequenceCompleted>().Metadata.Source);

        // Duplicate ClosedWon is a no-op — still one sequence.
        await session.EmitAsync(
            new OpportunityStageChanged(
                oppId,
                FromStage: "ClosedWon",
                ToStage: "ClosedWon",
                Amount: 85_000,
                ChampionEmail: "champ@northwind.test"),
            ct);

        await Task.Delay(100, ct);
        var afterDup = await ReadAsync(runnerId, ct);
        Assert.Single(afterDup.AllSaid<WinSequenceStarted>());
        Assert.Single(afterDup.AllSaid<WinSequenceCompleted>());
    }
}

public sealed class OpportunityCloseSequenceCancelTests(BrainTestClusters clusters)
    : DigitalBrainTest(clusters)
{
    protected override void Compose(DigitalBrainTestBuilder brain)
        => brain
            .AddModule<WinSequenceOpenRunner>()
            .AddModule<WinSequenceLedger>();

    [Fact(DisplayName =
        "Opportunity close sequence cancel: open sequence + stage revert → WinSequenceCancelled; no complete")]
    public async Task StageRevertCancelsOpenSequence()
    {
        var ct = Cancellation;
        var context = "win-cancel";
        var session = Brain.Session(context);
        var runnerId = new NeuronId("winsequenceopenrunner", context);
        var ledgerId = new NeuronId("winsequenceledger", context);
        var oppId = "opp-flip-1";

        await session.EmitAsync(
            new OpportunityStageChanged(
                oppId,
                FromStage: "Negotiation",
                ToStage: "ClosedWon",
                Amount: 10_000,
                ChampionEmail: "a@b.test"),
            ct);

        await WaitForJournalAsync(
            runnerId,
            reading => reading.AllSaid<WinSequenceStarted>().Count == 1
                && reading.AllSaid<WinThankYouEmailSent>().Count == 1,
            "open sequence started",
            ct);

        await session.EmitAsync(
            new OpportunityStageChanged(
                oppId,
                FromStage: "ClosedWon",
                ToStage: "Negotiation",
                Amount: 10_000,
                ChampionEmail: "a@b.test"),
            ct);

        var runner = await WaitForJournalAsync(
            runnerId,
            reading => reading.AllSaid<WinSequenceCancelled>().Count == 1,
            "sequence cancelled on stage revert",
            ct);

        await WaitForJournalAsync(
            ledgerId,
            reading => reading.AllHeard<WinSequenceCancelled>().Count == 1,
            "ledger heard cancel",
            ct);

        Assert.Empty(runner.AllSaid<WinSequenceCompleted>());
        Assert.Empty(runner.AllSaid<WinCsTaskCreated>());
        var cancel = Assert.IsType<WinSequenceCancelled>(
            runner.SaidSingle<WinSequenceCancelled>().Body);
        Assert.Equal(oppId, cancel.OppId);
        Assert.Contains("Negotiation", cancel.Reason, StringComparison.Ordinal);
    }
}
